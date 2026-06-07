using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;

namespace Pos.Application.Inventory;

/// <summary>One row of the reorder worklist: a tracked product at/below its reorder level. On-hand is
/// SUM(movements) — never stored; "low" is derived here, never a flag.</summary>
public sealed record LowStockLine(
    Guid ProductId, string Sku, string Name, UnitOfMeasure Unit,
    decimal OnHand, decimal ReorderLevel, decimal? SuggestedOrderQty);

/// <summary>
/// The manager's reorder worklist + the home-page badge count, both DERIVED on every read from
/// SUM(movements) vs the product's reorder level. Always reflects current state, so an item that dropped
/// below a level set only just now appears immediately (no event/flag required to surface it).
/// </summary>
public sealed class LowStockService
{
    private readonly ICurrentContext _ctx;
    private readonly IProductRepository _products;
    private readonly IStockMovementRepository _stock;

    public LowStockService(ICurrentContext ctx, IProductRepository products, IStockMovementRepository stock)
    {
        _ctx = ctx;
        _products = products;
        _stock = stock;
    }

    public async Task<IReadOnlyList<LowStockLine>> GetWorklistAsync(CancellationToken ct = default)
    {
        // Active products only — a deactivated line isn't a reorder candidate.
        var products = await _products.ListAsync(_ctx.TenantId, _ctx.StoreId, includeInactive: false, ct: ct);
        var onHand = await _stock.GetOnHandByProductAsync(_ctx.TenantId, _ctx.StoreId, ct);

        return products
            .Where(p => p.ReorderLevel is not null)
            .Select(p => (Product: p, OnHand: onHand.TryGetValue(p.Id, out var v) ? v : 0m, Level: p.ReorderLevel!.Value))
            .Where(x => x.OnHand <= x.Level)
            .OrderBy(x => x.OnHand - x.Level)          // most-below first (deficit ascending)
            .ThenBy(x => x.Product.Name)
            .Select(x => new LowStockLine(
                x.Product.Id, x.Product.Sku, x.Product.Name, x.Product.UnitOfMeasure,
                x.OnHand, x.Level, x.Product.ReorderQuantity))
            .ToList();
    }

    /// <summary>How many active products currently need reordering — the back-office badge.</summary>
    public async Task<int> CountAsync(CancellationToken ct = default) => (await GetWorklistAsync(ct)).Count;
}
