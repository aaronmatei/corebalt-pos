using Pos.Application.Abstractions;
using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

/// <summary>
/// Category master-data use cases, shared by the JSON API and the Blazor back-office (one home for the
/// create/update/activate orchestration — no duplicated logic). Name clashes within a parent throw
/// InvalidOperationException (→ 409); not-found returns null so the caller chooses 404 / an inline message.
/// </summary>
public sealed class CategoryService
{
    private readonly ICurrentContext _ctx;
    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _uow;

    public CategoryService(ICurrentContext ctx, ICategoryRepository categories, IUnitOfWork uow)
    {
        _ctx = ctx;
        _categories = categories;
        _uow = uow;
    }

    public Task<IReadOnlyList<Category>> ListAsync(bool includeInactive, CancellationToken ct = default) =>
        _categories.ListAsync(_ctx.TenantId, includeInactive, ct);

    public Task<Category?> GetAsync(Guid id, CancellationToken ct = default) =>
        _categories.GetAsync(_ctx.TenantId, id, ct);

    public async Task<Category> CreateAsync(string name, Guid? parentId, int displayOrder, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        if (parentId is { } p && await _categories.GetAsync(_ctx.TenantId, p, ct) is null)
            throw new InvalidOperationException("Parent category not found.");
        if (await _categories.NameExistsAsync(_ctx.TenantId, parentId, name, ct: ct))
            throw new InvalidOperationException($"A category named '{name}' already exists here.");

        var category = Category.Create(_ctx.TenantId, name, parentId, displayOrder);
        await _categories.AddAsync(category, ct);
        await _uow.SaveChangesAsync(ct);
        return category;
    }

    public async Task<Category?> UpdateAsync(Guid id, string name, Guid? parentId, int displayOrder,
        bool isActive, CancellationToken ct = default)
    {
        var category = await _categories.GetAsync(_ctx.TenantId, id, ct);
        if (category is null) return null;

        name = (name ?? "").Trim();
        if (await _categories.NameExistsAsync(_ctx.TenantId, parentId, name, excludingCategoryId: id, ct: ct))
            throw new InvalidOperationException($"A category named '{name}' already exists here.");

        category.Update(name, parentId, displayOrder);
        if (isActive) category.Reactivate(); else category.Deactivate();
        await _uow.SaveChangesAsync(ct);
        return category;
    }

    public async Task<Category?> SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        var category = await _categories.GetAsync(_ctx.TenantId, id, ct);
        if (category is null) return null;
        if (active) category.Reactivate(); else category.Deactivate();
        await _uow.SaveChangesAsync(ct);
        return category;
    }
}
