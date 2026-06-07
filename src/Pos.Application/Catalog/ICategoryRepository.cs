using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

public interface ICategoryRepository
{
    Task<Category?> GetAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default);

    /// <summary>
    /// Categories in the tenant, ordered by DisplayOrder then Name. Active only by default; pass
    /// includeInactive to also return soft-deleted ones (back-office views + report name resolution).
    /// </summary>
    Task<IReadOnlyList<Category>> ListAsync(Guid tenantId, bool includeInactive = false, CancellationToken ct = default);

    Task AddAsync(Category category, CancellationToken ct = default);

    /// <summary>
    /// Does another category in the tenant already use this name under the SAME parent? (Roots compare
    /// against roots.) excludingCategoryId skips the row being renamed.
    /// </summary>
    Task<bool> NameExistsAsync(Guid tenantId, Guid? parentId, string name, Guid? excludingCategoryId = null, CancellationToken ct = default);
}
