using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

/// <summary>The HQ catalogue master (M2), tenant-scoped.</summary>
public interface ICatalogItemRepository
{
    Task<CatalogItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<CatalogItem?> GetBySkuAsync(Guid tenantId, string sku, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogItem>> ListAsync(Guid tenantId, bool includeInactive, CancellationToken ct = default);
    Task AddAsync(CatalogItem item, CancellationToken ct = default);
}

/// <summary>The append-only catalogue change feed (M2) stores pull from.</summary>
public interface ICatalogChangeRepository
{
    Task AddAsync(CatalogChange change, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogChange>> ListSinceAsync(Guid tenantId, long sinceSeq, int max, CancellationToken ct = default);
}
