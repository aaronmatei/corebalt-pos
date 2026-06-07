using Pos.Application.Abstractions;
using Pos.Domain.Catalog;
using Pos.SharedKernel;

namespace Pos.Application.Catalog;

/// <summary>
/// Product/pricing use cases shared by the JSON API and the Blazor back-office — the single home for
/// the create/update/reprice/activate orchestration (uniqueness checks + domain calls + save), so the
/// two front ends never duplicate business logic. Conflicts throw InvalidOperationException (→ 409);
/// not-found returns null so the caller chooses 404 / an inline message.
/// </summary>
public sealed class ProductService
{
    private readonly ICurrentContext _ctx;
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _uow;

    public ProductService(ICurrentContext ctx, IProductRepository products, ICategoryRepository categories, IUnitOfWork uow)
    {
        _ctx = ctx;
        _products = products;
        _categories = categories;
        _uow = uow;
    }

    public Task<IReadOnlyList<Product>> ListAsync(bool includeInactive, Guid? categoryId = null, CancellationToken ct = default) =>
        _products.ListAsync(_ctx.TenantId, _ctx.StoreId, includeInactive, categoryId, ct);

    public Task<Product?> GetAsync(Guid id, CancellationToken ct = default) =>
        _products.GetAsync(_ctx.TenantId, _ctx.StoreId, id, ct);

    public async Task<Product> CreateAsync(string sku, string name, Money price, UnitOfMeasure unit,
        string? barcode, TaxClass taxClass, Guid? categoryId = null, CancellationToken ct = default)
    {
        sku = (sku ?? "").Trim();
        var bc = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        if (await _products.SkuExistsAsync(_ctx.TenantId, sku, ct: ct))
            throw new InvalidOperationException($"A product with SKU '{sku}' already exists.");
        if (bc is not null && await _products.BarcodeExistsAsync(_ctx.TenantId, bc, ct: ct))
            throw new InvalidOperationException($"A product with barcode '{bc}' already exists.");
        await ValidateCategoryAsync(categoryId, ct);

        var product = Product.Create(_ctx.TenantId, _ctx.StoreId, sku, name, price, unit, bc, taxClass, categoryId);
        await _products.AddAsync(product, ct);
        await _uow.SaveChangesAsync(ct);
        return product;
    }

    public async Task<Product?> UpdateAsync(Guid id, string name, string? barcode, UnitOfMeasure unit,
        TaxClass taxClass, bool isActive, Guid? categoryId = null,
        decimal? reorderLevel = null, decimal? reorderQuantity = null, CancellationToken ct = default)
    {
        var product = await _products.GetAsync(_ctx.TenantId, _ctx.StoreId, id, ct);
        if (product is null) return null;

        var bc = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        if (bc is not null && await _products.BarcodeExistsAsync(_ctx.TenantId, bc, excludingProductId: id, ct: ct))
            throw new InvalidOperationException($"A product with barcode '{bc}' already exists.");
        await ValidateCategoryAsync(categoryId, ct);

        product.UpdateDetails(name, bc, unit, taxClass, categoryId);
        product.SetReorderSettings(reorderLevel, reorderQuantity);
        if (isActive) product.Reactivate(); else product.Deactivate();
        await _uow.SaveChangesAsync(ct);
        return product;
    }

    /// <summary>A product may only point at a category that exists in the tenant (any active state — a
    /// product can stay in a now-inactive category). Null = Uncategorized, always allowed.</summary>
    private async Task ValidateCategoryAsync(Guid? categoryId, CancellationToken ct)
    {
        if (categoryId is { } id && await _categories.GetAsync(_ctx.TenantId, id, ct) is null)
            throw new InvalidOperationException("Category not found.");
    }

    /// <summary>Reprice via the domain rule — a real change raises ProductPriceChanged to the outbox.</summary>
    public async Task<Product?> RepriceAsync(Guid id, Money newPrice, CancellationToken ct = default)
    {
        var product = await _products.GetAsync(_ctx.TenantId, _ctx.StoreId, id, ct);
        if (product is null) return null;
        product.Reprice(newPrice, _ctx.UserId);
        await _uow.SaveChangesAsync(ct);
        return product;
    }

    public async Task<Product?> SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        var product = await _products.GetAsync(_ctx.TenantId, _ctx.StoreId, id, ct);
        if (product is null) return null;
        if (active) product.Reactivate(); else product.Deactivate();
        await _uow.SaveChangesAsync(ct);
        return product;
    }
}
