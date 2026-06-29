using Pos.Application.Abstractions;
using Pos.Domain.Catalog;
using Pos.SharedKernel;

namespace Pos.Application.Catalog;

/// <summary>
/// HQ-side catalogue management (M2). Every mutation updates the tenant's <see cref="CatalogItem"/> master
/// AND appends a full snapshot to the <see cref="CatalogChange"/> feed in the same transaction, so branch
/// store-servers can pull the delta. Tenant scope comes from the authenticated HQ manager (ICurrentContext).
/// </summary>
public sealed class CatalogItemService
{
    private readonly ICatalogItemRepository _items;
    private readonly ICatalogChangeRepository _changes;
    private readonly ICurrentContext _ctx;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public CatalogItemService(ICatalogItemRepository items, ICatalogChangeRepository changes,
        ICurrentContext ctx, IClock clock, IUnitOfWork uow)
    {
        _items = items;
        _changes = changes;
        _ctx = ctx;
        _clock = clock;
        _uow = uow;
    }

    public Task<IReadOnlyList<CatalogItem>> ListAsync(bool includeInactive = false, CancellationToken ct = default) =>
        _items.ListAsync(_ctx.TenantId, includeInactive, ct);

    public Task<CatalogItem?> GetAsync(Guid id, CancellationToken ct = default) =>
        _items.GetByIdAsync(_ctx.TenantId, id, ct);

    public async Task<CatalogItem> CreateAsync(string sku, string name, Money price, TaxClass taxClass,
        UnitOfMeasure unit, string? barcode, string? categoryName = null, CancellationToken ct = default)
    {
        sku = (sku ?? "").Trim();
        if (await _items.GetBySkuAsync(_ctx.TenantId, sku, ct) is not null)
            throw new InvalidOperationException($"SKU '{sku}' already exists in the catalogue.");
        var item = CatalogItem.Create(_ctx.TenantId, sku, name, price, taxClass, unit, barcode, categoryName, _clock.UtcNow);
        await _items.AddAsync(item, ct);
        await _changes.AddAsync(CatalogChange.From(item, _clock.UtcNow), ct);
        await _uow.SaveChangesAsync(ct);
        return item;
    }

    public async Task<CatalogItem?> UpdateAsync(Guid id, string name, string? barcode, UnitOfMeasure unit,
        TaxClass taxClass, string? categoryName = null, CancellationToken ct = default)
    {
        var item = await _items.GetByIdAsync(_ctx.TenantId, id, ct);
        if (item is null) return null;
        item.Update(name, barcode, unit, taxClass, categoryName, _clock.UtcNow);
        await _changes.AddAsync(CatalogChange.From(item, _clock.UtcNow), ct);
        await _uow.SaveChangesAsync(ct);
        return item;
    }

    public async Task<CatalogItem?> RepriceAsync(Guid id, Money price, CancellationToken ct = default)
    {
        var item = await _items.GetByIdAsync(_ctx.TenantId, id, ct);
        if (item is null) return null;
        item.Reprice(price, _clock.UtcNow);
        await _changes.AddAsync(CatalogChange.From(item, _clock.UtcNow), ct);
        await _uow.SaveChangesAsync(ct);
        return item;
    }

    public async Task<CatalogItem?> SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        var item = await _items.GetByIdAsync(_ctx.TenantId, id, ct);
        if (item is null) return null;
        item.SetActive(active, _clock.UtcNow);
        await _changes.AddAsync(CatalogChange.From(item, _clock.UtcNow), ct);
        await _uow.SaveChangesAsync(ct);
        return item;
    }
}
