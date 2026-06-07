using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Domain.Inventory;

namespace Pos.Application.Inventory;

/// <summary>A line in the store stock report — on-hand is always SUM(movements), never stored.</summary>
public sealed record StockReportLine(Guid ProductId, string Sku, string Name, Pos.Domain.Catalog.UnitOfMeasure Unit, bool IsActive, decimal OnHand);

/// <summary>
/// Stock receiving / adjustment / reporting shared by the API and the back-office. Each write appends
/// exactly one immutable StockMovement; on-hand is derived. Bad input throws ArgumentException (→ 400);
/// unknown product returns null (→ 404 / inline message).
/// </summary>
public sealed class StockService
{
    private readonly ICurrentContext _ctx;
    private readonly IProductRepository _products;
    private readonly IStockMovementRepository _stock;
    private readonly IUnitOfWork _uow;

    public StockService(ICurrentContext ctx, IProductRepository products, IStockMovementRepository stock, IUnitOfWork uow)
    {
        _ctx = ctx;
        _products = products;
        _stock = stock;
        _uow = uow;
    }

    public Task<decimal> GetOnHandAsync(Guid productId, CancellationToken ct = default) =>
        _stock.GetOnHandAsync(_ctx.TenantId, _ctx.StoreId, productId, ct);

    public async Task<(StockMovement Movement, decimal OnHand)?> ReceiveAsync(
        Guid productId, decimal quantity, StockMovementReason reason, string? reference, CancellationToken ct = default)
    {
        if (quantity <= 0) throw new ArgumentException("Receive quantity must be positive.", nameof(quantity));
        if (reason is not (StockMovementReason.Purchase or StockMovementReason.OpeningBalance or StockMovementReason.Adjustment))
            throw new ArgumentException("Receive reason must be Purchase, OpeningBalance or Adjustment.", nameof(reason));
        return await AppendAsync(productId, quantity, reason, reference, ct);
    }

    public async Task<(StockMovement Movement, decimal OnHand)?> AdjustAsync(
        Guid productId, decimal quantity, string? reference, CancellationToken ct = default)
    {
        if (quantity == 0) throw new ArgumentException("Adjustment quantity cannot be zero.", nameof(quantity));
        return await AppendAsync(productId, quantity, StockMovementReason.Adjustment, reference, ct);
    }

    public async Task<IReadOnlyList<StockReportLine>> GetReportAsync(CancellationToken ct = default)
    {
        var all = await _products.ListAsync(_ctx.TenantId, _ctx.StoreId, includeInactive: true, ct: ct);
        var onHand = await _stock.GetOnHandByProductAsync(_ctx.TenantId, _ctx.StoreId, ct);
        return all.Select(p => new StockReportLine(
            p.Id, p.Sku, p.Name, p.UnitOfMeasure, p.IsActive,
            onHand.TryGetValue(p.Id, out var v) ? v : 0m)).ToList();
    }

    private async Task<(StockMovement, decimal)?> AppendAsync(Guid productId, decimal delta, StockMovementReason reason, string? reference, CancellationToken ct)
    {
        if (await _products.GetAsync(_ctx.TenantId, _ctx.StoreId, productId, ct) is null) return null;
        var movement = StockMovement.Record(_ctx.TenantId, _ctx.StoreId, productId, delta, reason, sourceRef: null, reference: reference);
        await _stock.AddAsync(movement, ct);
        await _uow.SaveChangesAsync(ct);
        var onHand = await _stock.GetOnHandAsync(_ctx.TenantId, _ctx.StoreId, productId, ct);
        return (movement, onHand);
    }
}
