using Microsoft.EntityFrameworkCore;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class CatalogItemRepository : ICatalogItemRepository
{
    private readonly PosDbContext _db;
    public CatalogItemRepository(PosDbContext db) => _db = db;

    public Task<CatalogItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _db.CatalogItems.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);

    public Task<CatalogItem?> GetBySkuAsync(Guid tenantId, string sku, CancellationToken ct = default) =>
        _db.CatalogItems.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Sku == sku, ct);

    public async Task<IReadOnlyList<CatalogItem>> ListAsync(Guid tenantId, bool includeInactive, CancellationToken ct = default) =>
        await _db.CatalogItems
            .Where(c => c.TenantId == tenantId && (includeInactive || c.IsActive))
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public async Task AddAsync(CatalogItem item, CancellationToken ct = default) =>
        await _db.CatalogItems.AddAsync(item, ct);
}

internal sealed class CatalogPullStateRepository : ICatalogPullStateRepository
{
    private readonly PosDbContext _db;
    public CatalogPullStateRepository(PosDbContext db) => _db = db;

    public Task<CatalogPullState?> GetAsync(Guid tenantId, Guid storeId, CancellationToken ct = default) =>
        _db.CatalogPullStates.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.StoreId == storeId, ct);

    public async Task AddAsync(CatalogPullState state, CancellationToken ct = default) =>
        await _db.CatalogPullStates.AddAsync(state, ct);
}

internal sealed class CatalogChangeRepository : ICatalogChangeRepository
{
    private readonly PosDbContext _db;
    public CatalogChangeRepository(PosDbContext db) => _db = db;

    public async Task AddAsync(CatalogChange change, CancellationToken ct = default) =>
        await _db.CatalogChanges.AddAsync(change, ct);

    public async Task<IReadOnlyList<CatalogChange>> ListSinceAsync(Guid tenantId, long sinceSeq, int max, CancellationToken ct = default) =>
        await _db.CatalogChanges
            .Where(c => c.TenantId == tenantId && c.Seq > sinceSeq)
            .OrderBy(c => c.Seq)
            .Take(Math.Clamp(max, 1, 500))
            .ToListAsync(ct);
}
