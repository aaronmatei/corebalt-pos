using Microsoft.EntityFrameworkCore;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class CategoryRepository : ICategoryRepository
{
    private readonly PosDbContext _db;
    public CategoryRepository(PosDbContext db) => _db = db;

    public Task<Category?> GetAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default) =>
        _db.Categories
            .Where(c => c.TenantId == tenantId && c.Id == categoryId)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Category>> ListAsync(Guid tenantId, bool includeInactive = false, CancellationToken ct = default) =>
        await _db.Categories
            .Where(c => c.TenantId == tenantId && (includeInactive || c.IsActive))
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);

    public async Task AddAsync(Category category, CancellationToken ct = default) =>
        await _db.Categories.AddAsync(category, ct);

    public Task<bool> NameExistsAsync(Guid tenantId, Guid? parentId, string name, Guid? excludingCategoryId = null, CancellationToken ct = default) =>
        _db.Categories.AnyAsync(c => c.TenantId == tenantId && c.ParentId == parentId && c.Name == name
            && (excludingCategoryId == null || c.Id != excludingCategoryId), ct);
}
