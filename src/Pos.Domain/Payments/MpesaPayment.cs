using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Payments;

/// <summary>
/// The reconciliation ledger for a single M-Pesa STK-push attempt — the durable link between a
/// sale's pending <c>Tender</c> and Daraja's asynchronous result. Keyed for lookup by
/// <see cref="CheckoutRequestId"/> (globally unique, issued by Daraja) so the callback can be made
/// strictly idempotent and reconciled by id + amount without a public URL. State transitions
/// Pending → Confirmed/Failed exactly once; re-applying the same terminal result is a no-op.
///
/// Carries TenantId/StoreId (invariants #2/#4) and a UUIDv7 id (#1) like every other fact. We store
/// only a MASKED MSISDN — never the full phone number — so the payments table isn't a PII trove.
/// </summary>
public sealed class MpesaPayment : AggregateRoot, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid TenderId { get; private set; }

    public string CheckoutRequestId { get; private set; } = string.Empty;
    public string? MerchantRequestId { get; private set; }
    public string MsisdnMasked { get; private set; } = string.Empty;
    public Money Amount { get; private set; } = Money.Zero();

    public MpesaPaymentStatus Status { get; private set; }
    public int? ResultCode { get; private set; }
    public string? ResultDescription { get; private set; }
    public string? MpesaReceiptNumber { get; private set; }

    public DateTimeOffset InitiatedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    private MpesaPayment() { } // EF

    public static MpesaPayment Initiate(
        Guid tenantId, Guid storeId, Guid saleId, Guid tenderId,
        string checkoutRequestId, string? merchantRequestId, string msisdnMasked,
        Money amount, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(checkoutRequestId))
            throw new ArgumentException("CheckoutRequestId is required.", nameof(checkoutRequestId));

        return new MpesaPayment
        {
            Id = Uuid7.NewGuid(),
            TenantId = tenantId,
            StoreId = storeId,
            SaleId = saleId,
            TenderId = tenderId,
            CheckoutRequestId = checkoutRequestId,
            MerchantRequestId = merchantRequestId,
            MsisdnMasked = msisdnMasked,
            Amount = amount,
            Status = MpesaPaymentStatus.Pending,
            InitiatedAtUtc = nowUtc
        };
    }

    public bool IsPending => Status == MpesaPaymentStatus.Pending;

    /// <summary>Record a successful result. Idempotent — re-applying after success does nothing.</summary>
    public void Confirm(string? mpesaReceipt, int resultCode, string? resultDescription, DateTimeOffset nowUtc)
    {
        if (Status == MpesaPaymentStatus.Confirmed) return;
        if (Status == MpesaPaymentStatus.Failed)
            throw new InvalidOperationException("A failed M-Pesa payment cannot be confirmed.");
        Status = MpesaPaymentStatus.Confirmed;
        MpesaReceiptNumber = mpesaReceipt;
        ResultCode = resultCode;
        ResultDescription = resultDescription;
        CompletedAtUtc = nowUtc;
    }

    /// <summary>Record a terminal failure. Idempotent — re-applying after failure does nothing.</summary>
    public void Fail(int resultCode, string? resultDescription, DateTimeOffset nowUtc)
    {
        if (Status == MpesaPaymentStatus.Failed) return;
        if (Status == MpesaPaymentStatus.Confirmed)
            throw new InvalidOperationException("A confirmed M-Pesa payment cannot be failed.");
        Status = MpesaPaymentStatus.Failed;
        ResultCode = resultCode;
        ResultDescription = resultDescription;
        CompletedAtUtc = nowUtc;
    }
}
