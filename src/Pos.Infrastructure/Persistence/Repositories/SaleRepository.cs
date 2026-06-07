using Microsoft.EntityFrameworkCore;
using Pos.Application.Sales;
using Pos.Domain.Sales;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class SaleRepository : ISaleRepository
{
    private readonly PosDbContext _db;
    public SaleRepository(PosDbContext db) => _db = db;

    public Task<Sale?> GetAsync(Guid tenantId, Guid storeId, Guid saleId, CancellationToken ct = default) =>
        _db.Sales
            // Lines and Tenders are owned collections, so EF always loads both with the Sale.
            // Pulling two collections in one SQL statement is a cartesian product (the
            // MultipleCollectionIncludeWarning); SplitQuery fetches each in its own round-trip.
            .AsSplitQuery()
            .Where(s => s.TenantId == tenantId && s.StoreId == storeId && s.Id == saleId)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(Sale sale, CancellationToken ct = default)
    {
        await _db.Sales.AddAsync(sale, ct);
    }

    public async Task<IReadOnlyList<Sale>> ListBySessionAsync(Guid tenantId, Guid storeId, Guid sessionId, CancellationToken ct = default) =>
        await _db.Sales
            .AsSplitQuery()
            .Where(s => s.TenantId == tenantId && s.StoreId == storeId
                && s.RegisterSessionId == sessionId && s.Status == SaleStatus.Completed)
            .OrderBy(s => s.CompletedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Sale>> ListCompletedBetweenAsync(Guid tenantId, Guid storeId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default) =>
        await _db.Sales
            .AsSplitQuery()
            .Where(s => s.TenantId == tenantId && s.StoreId == storeId && s.Status == SaleStatus.Completed
                && s.CompletedAtUtc >= fromUtc && s.CompletedAtUtc < toUtc)
            .OrderBy(s => s.CompletedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Sale>> ListByFiscalStatusAsync(FiscalStatus status, int max, CancellationToken ct = default) =>
        await _db.Sales
            .AsSplitQuery()
            .Where(s => s.FiscalStatus == status)
            .OrderBy(s => s.CreatedAtUtc)
            .Take(max)
            .ToListAsync(ct);
}
