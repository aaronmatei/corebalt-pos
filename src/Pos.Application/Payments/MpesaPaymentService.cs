using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Sales;
using Pos.Domain.Payments;
using Pos.Domain.Sales;
using Pos.SharedKernel;

namespace Pos.Application.Payments;

/// <summary>
/// Orchestrates the ASYNCHRONOUS M-Pesa flow: initiate (open sale + pending tender + STK push),
/// query (poll Daraja and reconcile), and callback (idempotent reconcile by CheckoutRequestID).
/// A sale with an M-Pesa tender is only finalized — completed + stock movements written — once the
/// pending tender is Confirmed and the basket is fully paid, so we never "fake" the payment as
/// synchronous. Kept separate from CheckoutService so the cash-only checkout path carries no
/// payment-provider dependency.
/// </summary>
public sealed class MpesaPaymentService
{
    private readonly ICurrentContext _ctx;
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IMpesaPaymentRepository _payments;
    private readonly IMpesaClient _mpesa;
    private readonly SaleCompletion _completion;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public MpesaPaymentService(
        ICurrentContext ctx, ISaleRepository sales, IProductRepository products,
        IMpesaPaymentRepository payments, IMpesaClient mpesa, SaleCompletion completion,
        IClock clock, IUnitOfWork uow)
    {
        _ctx = ctx; _sales = sales; _products = products;
        _payments = payments; _mpesa = mpesa; _completion = completion; _clock = clock; _uow = uow;
    }

    /// <summary>
    /// Build the sale (lines priced from the catalogue, plus any already-confirmed cash tenders for
    /// a split payment), attach a PENDING M-Pesa tender, persist the open sale, then fire the STK
    /// push and record the reconciliation row. The sale stays Open/pending — confirmation happens
    /// later via <see cref="QueryStatusAsync"/> or <see cref="HandleCallbackAsync"/>.
    /// </summary>
    public async Task<MpesaInitiationResult> InitiateAsync(
        Guid registerId,
        string currency,
        IReadOnlyList<CheckoutLine> lines,
        IReadOnlyList<CheckoutTender> cashTenders,
        decimal mpesaAmount,
        string phoneNumber,
        string accountReference,
        CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("A checkout needs at least one line.", nameof(lines));
        if (mpesaAmount <= 0)
            throw new ArgumentException("The M-Pesa amount must be positive.", nameof(mpesaAmount));
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("A phone number is required for M-Pesa.", nameof(phoneNumber));

        var sale = Sale.Start(_ctx.TenantId, _ctx.StoreId, registerId, _ctx.UserId, currency);

        foreach (var l in lines)
        {
            var product = await _products.GetAsync(_ctx.TenantId, _ctx.StoreId, l.ProductId, ct)
                ?? throw new InvalidOperationException($"Product {l.ProductId} not found in this store.");
            if (!product.IsActive) throw new InvalidOperationException($"Product {product.Sku} is inactive.");
            var unitPrice = new Money(product.Price.Amount, product.Price.Currency);
            sale.AddLine(product.Id, product.Name, l.Quantity, unitPrice, product.TaxClass, product.UnitOfMeasure);
        }

        foreach (var t in cashTenders ?? Array.Empty<CheckoutTender>())
            sale.AddTender(t.Type, new Money(t.Amount, currency), t.Reference);

        var tenderId = sale.AddPendingTender(TenderType.Mpesa, new Money(mpesaAmount, currency));

        await _sales.AddAsync(sale, ct);
        await _uow.SaveChangesAsync(ct); // durable OPEN sale + pending tender BEFORE we prompt the customer

        var push = await _mpesa.StkPushAsync(
            new StkPushRequest(mpesaAmount, phoneNumber, accountReference, "POS Sale"), ct);

        if (!push.Ok || string.IsNullOrWhiteSpace(push.CheckoutRequestId))
        {
            sale.FailTender(tenderId);
            await _uow.SaveChangesAsync(ct);
            return new MpesaInitiationResult(sale.Id, tenderId, null, nameof(MpesaPaymentStatus.Failed),
                push.Error ?? push.ResponseDescription ?? "M-Pesa STK push was rejected.");
        }

        sale.SetTenderProviderReference(tenderId, push.CheckoutRequestId);
        var payment = MpesaPayment.Initiate(
            sale.TenantId, sale.StoreId, sale.Id, tenderId,
            push.CheckoutRequestId, push.MerchantRequestId, Mask(phoneNumber),
            new Money(mpesaAmount, currency), _clock.UtcNow);
        await _payments.AddAsync(payment, ct);
        await _uow.SaveChangesAsync(ct);

        return new MpesaInitiationResult(sale.Id, tenderId, push.CheckoutRequestId, nameof(MpesaPaymentStatus.Pending), null);
    }

    /// <summary>Poll Daraja's STK query for a sale's pending payment and reconcile. Idempotent once terminal.</summary>
    public async Task<MpesaStatusResult> QueryStatusAsync(Guid saleId, CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(_ctx.TenantId, _ctx.StoreId, saleId, ct)
            ?? throw new InvalidOperationException($"Sale {saleId} not found in this store.");
        var payment = await _payments.FindBySaleIdAsync(_ctx.TenantId, _ctx.StoreId, saleId, ct)
            ?? throw new InvalidOperationException($"No M-Pesa payment found for sale {saleId}.");

        if (payment.IsPending)
        {
            var q = await _mpesa.StkQueryAsync(payment.CheckoutRequestId, ct);
            await _uow.ExecuteInTransactionAsync(async ct2 =>
            {
                ApplyResult(sale, payment, q.State, q.ResultCode, q.ResultDescription, q.MpesaReceipt);
                await TryFinalizeAsync(sale, ct2); // assigns receipt number + completes when fully paid
                await _uow.SaveChangesAsync(ct2);
            }, ct);
        }

        return BuildStatus(sale, payment);
    }

    /// <summary>
    /// Reconcile a Daraja callback. Idempotent (terminal payments are a no-op) and reconciled
    /// strictly by CheckoutRequestID + amount, so a replayed or spoofed callback can't double-confirm.
    /// </summary>
    public async Task<MpesaCallbackOutcome> HandleCallbackAsync(MpesaCallback cb, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cb.CheckoutRequestId))
            return new MpesaCallbackOutcome(false, "Missing CheckoutRequestID.");

        var payment = await _payments.FindByCheckoutRequestIdAsync(cb.CheckoutRequestId, ct);
        if (payment is null)
            return new MpesaCallbackOutcome(false, "Unknown CheckoutRequestID.");
        if (!payment.IsPending)
            return new MpesaCallbackOutcome(true, "Already reconciled.");

        if (cb.Amount is decimal amount && amount != payment.Amount.Amount)
            return new MpesaCallbackOutcome(false, "Amount mismatch; callback ignored.");

        var sale = await _sales.GetAsync(payment.TenantId, payment.StoreId, payment.SaleId, ct)
            ?? throw new InvalidOperationException($"Sale {payment.SaleId} not found for payment {payment.Id}.");

        var state = cb.ResultCode == 0 ? MpesaQueryState.Success : MpesaQueryState.Failed;
        await _uow.ExecuteInTransactionAsync(async ct2 =>
        {
            ApplyResult(sale, payment, state, cb.ResultCode, cb.ResultDescription, cb.MpesaReceipt);
            await TryFinalizeAsync(sale, ct2);
            await _uow.SaveChangesAsync(ct2);
        }, ct);

        return new MpesaCallbackOutcome(true, "Reconciled.");
    }

    private void ApplyResult(Sale sale, MpesaPayment payment, MpesaQueryState state,
        int resultCode, string? description, string? receipt)
    {
        switch (state)
        {
            case MpesaQueryState.Success:
                sale.ConfirmTender(payment.TenderId, receipt);
                payment.Confirm(receipt, resultCode, description, _clock.UtcNow);
                break;
            case MpesaQueryState.Failed:
                sale.FailTender(payment.TenderId);
                payment.Fail(resultCode, description, _clock.UtcNow);
                break;
            default:
                break; // Processing — still waiting on the customer; leave everything pending
        }
    }

    private async Task TryFinalizeAsync(Sale sale, CancellationToken ct)
    {
        if (!sale.IsFullyPaid) return;
        // Shared finalization: stamps the receipt number, completes the sale, writes stock movements
        // — all within the caller's transaction so the receipt-number increment commits atomically.
        await _completion.FinalizeAsync(sale, ct);
    }

    private static MpesaStatusResult BuildStatus(Sale sale, MpesaPayment payment)
    {
        var change = sale.BalanceDue.Amount < 0 ? -sale.BalanceDue.Amount : 0m;
        return new MpesaStatusResult(
            sale.Id,
            payment.CheckoutRequestId,
            payment.Status.ToString(),
            sale.Status.ToString(),
            payment.ResultDescription,
            payment.MpesaReceiptNumber,
            sale.Subtotal.Amount,
            change,
            sale.Currency);
    }

    private static string Mask(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 6) return "******";
        return string.Concat(digits.AsSpan(0, 3), new string('*', digits.Length - 6), digits.AsSpan(digits.Length - 3));
    }
}

public sealed record MpesaInitiationResult(Guid SaleId, Guid TenderId, string? CheckoutRequestId, string Status, string? Message);
public sealed record MpesaStatusResult(Guid SaleId, string? CheckoutRequestId, string PaymentStatus, string SaleStatus, string? ResultDescription, string? Receipt, decimal? Total, decimal? ChangeDue, string Currency);
public sealed record MpesaCallback(string CheckoutRequestId, string? MerchantRequestId, int ResultCode, string? ResultDescription, decimal? Amount, string? MpesaReceipt);
public sealed record MpesaCallbackOutcome(bool Accepted, string Message);
