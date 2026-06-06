using Pos.Domain.Inventory;

namespace Pos.Application.Inventory;

public interface IStockMovementRepository
{
    Task AddAsync(StockMovement movement, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<StockMovement> movements, CancellationToken ct = default);

    /// <summary>
    /// Stock on hand = SUM(QuantityDelta) for (tenant, store, product). Pushed to the
    /// database so it stays correct under concurrent appends — the count is never
    /// materialized into a mutable column.
    /// </summary>
    Task<decimal> GetOnHandAsync(Guid tenantId, Guid storeId, Guid productId, CancellationToken ct = default);

    /// <summary>On-hand per product for the whole store (productId → SUM of movements). For the stock report.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetOnHandByProductAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);
}
