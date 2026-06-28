using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

/// <summary>Store-side cursor into the HQ catalogue feed (M2).</summary>
public interface ICatalogPullStateRepository
{
    Task<CatalogPullState?> GetAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);
    Task AddAsync(CatalogPullState state, CancellationToken ct = default);
}
