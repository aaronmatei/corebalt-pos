using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Inventory;
using Pos.Domain.Inventory;
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
    private readonly IStockMovementRepository _stock;
    private readonly IUnitOfWork _uow;

    public CheckoutService(
        ICurrentContext ctx,
        ISaleRepository sales,
        IProductRepository products,
        IStockMovementRepository stock,
        IUnitOfWork uow)
    { _ctx = ctx; _sales = sales; _products = products; _stock = stock; _uow = uow; }

    /// <summary>Open a fresh sale on a register. Returns the sale id (UUIDv7).</summary>
    public async Task<Guid> StartAsync(Guid registerId, string currency = "KES", CancellationToken ct = default)
    {
        var sale = Sale.Start(_ctx.TenantId, _ctx.StoreId, registerId, _ctx.UserId, currency);
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
        sale.AddLine(product.Id, product.Name, quantity, unitPrice);
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
    /// Complete a sale. Also writes one negative-delta StockMovement per line in the SAME
    /// unit of work, so a crash during checkout either commits the completed sale + the
    /// movements together, or commits neither — that's how the append-only inventory
    /// invariant survives partial failure.
    /// </summary>
    public async Task<CheckoutResult> CompleteAsync(Guid saleId, CancellationToken ct = default)
    {
        var sale = await RequireSaleAsync(saleId, ct);
        sale.Complete();

        var movements = sale.Lines.Select(line => StockMovement.Record(
            _ctx.TenantId, _ctx.StoreId, line.ProductId,
            -line.Quantity, StockMovementReason.Sale, sourceRef: sale.Id));
        await _stock.AddRangeAsync(movements, ct);

        await _uow.SaveChangesAsync(ct);

        var change = sale.BalanceDue.Amount < 0 ? -sale.BalanceDue.Amount : 0m;
        return new CheckoutResult(sale.Id, sale.Subtotal.Amount, change, sale.Currency);
    }

    private async Task<Sale> RequireSaleAsync(Guid saleId, CancellationToken ct) =>
        await _sales.GetAsync(_ctx.TenantId, _ctx.StoreId, saleId, ct)
        ?? throw new InvalidOperationException($"Sale {saleId} not found in this store.");
}

public sealed record CheckoutResult(Guid SaleId, decimal Total, decimal ChangeDue, string Currency);
