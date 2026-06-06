using Microsoft.EntityFrameworkCore;
using Pos.Application.Inventory;
using Pos.Domain.Inventory;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class StockMovementRepository : IStockMovementRepository
{
    private readonly PosDbContext _db;
    public StockMovementRepository(PosDbContext db) => _db = db;

    public async Task AddAsync(StockMovement movement, CancellationToken ct = default) =>
        await _db.StockMovements.AddAsync(movement, ct);

    public async Task AddRangeAsync(IEnumerable<StockMovement> movements, CancellationToken ct = default) =>
        await _db.StockMovements.AddRangeAsync(movements, ct);

    public Task<decimal> GetOnHandAsync(Guid tenantId, Guid storeId, Guid productId, CancellationToken ct = default) =>
        _db.StockMovements
            .Where(m => m.TenantId == tenantId && m.StoreId == storeId && m.ProductId == productId)
            .SumAsync(m => m.QuantityDelta, ct);
}
