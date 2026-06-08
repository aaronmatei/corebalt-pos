using Pos.Application.Abstractions;
using Pos.Application.Cash;
using Pos.Application.Catalog;
using Pos.Application.Customers;
using Pos.Application.Tenancy;
using Pos.Domain.Sales;
using Pos.SharedKernel;

namespace Pos.Application.Sales;

/// <summary>
/// The store-tier orchestration for a single checkout. Holds no state itself — each call
/// runs inside a unit of work and commits atomically. CheckoutService is the only place
/// that knows the rules for crossing aggregates (sale completion fans out into stock
/// movements); the API layer in step 3 maps endpoints directly to its methods.
/// </summary>
public sealed class CheckoutService
{
    private readonly ICurrentContext _ctx;
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IRegisterRepository _registers;
    private readonly IRegisterSessionRepository _sessions;
    private readonly SaleCompletion _completion;
    private readonly ISetupGuard _setup;
    private readonly ICustomerRepository _customers;
    private readonly LoyaltyOptions _loyalty;
    private readonly IUnitOfWork _uow;

    public CheckoutService(
        ICurrentContext ctx,
        ISaleRepository sales,
        IProductRepository products,
        IRegisterRepository registers,
        IRegisterSessionRepository sessions,
        SaleCompletion completion,
        ISetupGuard setup,
        ICustomerRepository customers,
        LoyaltyOptions loyalty,
        IUnitOfWork uow)
    { _ctx = ctx; _sales = sales; _products = products; _registers = registers; _sessions = sessions; _completion = completion; _setup = setup; _customers = customers; _loyalty = loyalty; _uow = uow; }

    /// <summary>The register must have an OPEN shift to sell — otherwise the cashier opens one first.</summary>
    private async Task<Guid> RequireOpenSessionAsync(Guid registerId, CancellationToken ct) =>
        (await _sessions.GetOpenAsync(_ctx.TenantId, _ctx.StoreId, registerId, ct))?.Id
        ?? throw new InvalidOperationException("No open register session; open a shift (enter the opening float) before selling.");

    /// <summary>Open a fresh sale on a register. Returns the sale id (UUIDv7).</summary>
    public async Task<Guid> StartAsync(Guid registerId, string currency = "KES", CancellationToken ct = default)
    {
        await _setup.EnsureConfiguredAsync(ct);
        var sessionId = await RequireOpenSessionAsync(registerId, ct);
        var register = await _registers.GetOrCreateAsync(_ctx.TenantId, _ctx.StoreId, registerId, ct);
        var sale = Sale.Start(_ctx.TenantId, _ctx.StoreId, registerId, _ctx.UserId, currency, _ctx.UserName, _ctx.StaffCode, register.DisplayLabel, sessionId);
        await _sales.AddAsync(sale, ct);
        await _uow.SaveChangesAsync(ct);
        return sale.Id;
    }

    /// <summary>
    /// Add a line by product id. Description and unit price come from the Product so the
    /// client cannot inject a different price; the till is the source for quantity only.
    /// </summary>
    public async Task AddLineAsync(Guid saleId, Guid productId, decimal quantity, CancellationToken ct = default)
    {
        var sale = await RequireSaleAsync(saleId, ct);
        var product = await _products.GetAsync(_ctx.TenantId, _ctx.StoreId, productId, ct)
            ?? throw new InvalidOperationException($"Product {productId} not found in this store.");
        if (!product.IsActive) throw new InvalidOperationException($"Product {product.Sku} is inactive.");

        // Defensive copy: Money is an EF-owned type, and passing product.Price by reference
        // makes EF track the same instance under both Product.Price and SaleLine.UnitPrice,
        // which corrupts change tracking (the new line gets staged as Modified, not Added).
        var unitPrice = new Money(product.Price.Amount, product.Price.Currency);
        sale.AddLine(product.Id, product.Name, quantity, unitPrice, product.TaxClass, product.UnitOfMeasure);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task AddTenderAsync(Guid saleId, TenderType type, decimal amount,
        string? reference = null, CancellationToken ct = default)
    {
        var sale = await RequireSaleAsync(saleId, ct);
        sale.AddTender(type, new Money(amount, sale.Currency), reference);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Complete a sale. Stamps the receipt number, writes one negative-delta StockMovement per line,
    /// and commits — all in ONE transaction, so the receipt-number increment, the completed sale, the
    /// movements and the outbox rows commit together or not at all.
    /// </summary>
    public async Task<CheckoutResult> CompleteAsync(Guid saleId, CancellationToken ct = default)
    {
        var sale = await RequireSaleAsync(saleId, ct);
        return await _uow.ExecuteInTransactionAsync(async ct2 =>
        {
            await _completion.FinalizeAsync(sale, ct2);
            await _uow.SaveChangesAsync(ct2);
            var change = sale.BalanceDue.Amount < 0 ? -sale.BalanceDue.Amount : 0m;
            return new CheckoutResult(sale.Id, sale.Subtotal.Amount, change, sale.Currency);
        }, ct);
    }

    /// <summary>
    /// One-shot checkout: open a sale, add every line (price always sourced from the Product),
    /// add every tender, complete, and write the stock movements — all in a SINGLE unit of work.
    /// A till call either commits the whole sale or nothing, so a dropped connection can't leave
    /// a half-built open sale on the server. The per-step methods above remain for flows that
    /// build a sale incrementally (e.g. a parked/resumed basket).
    /// </summary>
    public async Task<CheckoutResult> CheckoutAsync(
        Guid registerId,
        string currency,
        IReadOnlyList<CheckoutLine> lines,
        IReadOnlyList<CheckoutTender> tenders,
        CancellationToken ct = default,
        Guid saleId = default,
        Guid? customerId = null)
    {
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("A checkout needs at least one line.", nameof(lines));

        // Idempotent replay: if the till already committed this sale (it generates the UUIDv7 and may
        // re-POST a sale it queued offline, or retry after a dropped response), return the existing one
        // instead of charging twice. Checked FIRST — a committed sale stands even if the shift has since
        // closed, so this must not depend on the setup/open-session guards below.
        if (saleId != default)
        {
            var existing = await _sales.GetAsync(_ctx.TenantId, _ctx.StoreId, saleId, ct);
            if (existing is not null)
            {
                var changeDue = existing.BalanceDue.Amount < 0 ? -existing.BalanceDue.Amount : 0m;
                return new CheckoutResult(existing.Id, existing.Subtotal.Amount, changeDue, existing.Currency);
            }
        }

        await _setup.EnsureConfiguredAsync(ct);
        var sessionId = await RequireOpenSessionAsync(registerId, ct);

        // Attach a loyalty member if one was supplied and exists in this tenant (else sell as walk-in —
        // an unknown/stale id never blocks the sale).
        var customer = customerId is { } cid ? await _customers.GetAsync(_ctx.TenantId, cid, ct) : null;

        var register = await _registers.GetOrCreateAsync(_ctx.TenantId, _ctx.StoreId, registerId, ct);
        var sale = Sale.Start(_ctx.TenantId, _ctx.StoreId, registerId, _ctx.UserId, currency, _ctx.UserName, _ctx.StaffCode, register.DisplayLabel, sessionId, saleId, customer?.Id);

        foreach (var l in lines)
        {
            var product = await _products.GetAsync(_ctx.TenantId, _ctx.StoreId, l.ProductId, ct)
                ?? throw new InvalidOperationException($"Product {l.ProductId} not found in this store.");
            if (!product.IsActive) throw new InvalidOperationException($"Product {product.Sku} is inactive.");

            // Defensive copy of the owned Money — see AddLineAsync for why sharing the instance
            // corrupts change tracking.
            var unitPrice = new Money(product.Price.Amount, product.Price.Currency);
            sale.AddLine(product.Id, product.Name, l.Quantity, unitPrice, product.TaxClass, product.UnitOfMeasure);
        }

        foreach (var t in tenders ?? Array.Empty<CheckoutTender>())
            sale.AddTender(t.Type, new Money(t.Amount, sale.Currency), t.Reference);

        return await _uow.ExecuteInTransactionAsync(async ct2 =>
        {
            await _completion.FinalizeAsync(sale, ct2); // receipt number + complete + stock movements
            await _sales.AddAsync(sale, ct2);

            // Loyalty accrual — points on the VAT-inclusive grand total, in the SAME transaction as the sale
            // (the customer is already tracked, so mutating it flushes with everything else).
            if (customer is not null)
                customer.AccruePoints(_loyalty.PointsFor(sale.GrandTotal.Amount));

            await _uow.SaveChangesAsync(ct2); // atomic: counter + sale + lines + tenders + movements + outbox + points
            var change = sale.BalanceDue.Amount < 0 ? -sale.BalanceDue.Amount : 0m;
            return new CheckoutResult(sale.Id, sale.Subtotal.Amount, change, sale.Currency);
        }, ct);
    }

    private async Task<Sale> RequireSaleAsync(Guid saleId, CancellationToken ct) =>
        await _sales.GetAsync(_ctx.TenantId, _ctx.StoreId, saleId, ct)
        ?? throw new InvalidOperationException($"Sale {saleId} not found in this store.");
}

public sealed record CheckoutLine(Guid ProductId, decimal Quantity);
public sealed record CheckoutTender(TenderType Type, decimal Amount, string? Reference = null);
public sealed record CheckoutResult(Guid SaleId, decimal Total, decimal ChangeDue, string Currency);
