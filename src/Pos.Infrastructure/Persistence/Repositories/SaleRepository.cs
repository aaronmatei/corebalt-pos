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
}
