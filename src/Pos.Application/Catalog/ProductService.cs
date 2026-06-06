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
    private readonly IUnitOfWork _uow;

    public ProductService(ICurrentContext ctx, IProductRepository products, IUnitOfWork uow)
    {
        _ctx = ctx;
        _products = products;
        _uow = uow;
    }

    public Task<IReadOnlyList<Product>> ListAsync(bool includeInactive, CancellationToken ct = default) =>
        _products.ListAsync(_ctx.TenantId, _ctx.StoreId, includeInactive, ct);

    public Task<Product?> GetAsync(Guid id, CancellationToken ct = default) =>
        _products.GetAsync(_ctx.TenantId, _ctx.StoreId, id, ct);

    public async Task<Product> CreateAsync(string sku, string name, Money price, UnitOfMeasure unit,
        string? barcode, TaxClass taxClass, CancellationToken ct = default)
    {
        sku = (sku ?? "").Trim();
        var bc = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        if (await _products.SkuExistsAsync(_ctx.TenantId, sku, ct: ct))
            throw new InvalidOperationException($"A product with SKU '{sku}' already exists.");
        if (bc is not null && await _products.BarcodeExistsAsync(_ctx.TenantId, bc, ct: ct))
            throw new InvalidOperationException($"A product with barcode '{bc}' already exists.");

        var product = Product.Create(_ctx.TenantId, _ctx.StoreId, sku, name, price, unit, bc, taxClass);
        await _products.AddAsync(product, ct);
        await _uow.SaveChangesAsync(ct);
        return product;
    }

    public async Task<Product?> UpdateAsync(Guid id, string name, string? barcode, UnitOfMeasure unit,
        TaxClass taxClass, bool isActive, CancellationToken ct = default)
    {
        var product = await _products.GetAsync(_ctx.TenantId, _ctx.StoreId, id, ct);
        if (product is null) return null;

        var bc = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        if (bc is not null && await _products.BarcodeExistsAsync(_ctx.TenantId, bc, excludingProductId: id, ct: ct))
            throw new InvalidOperationException($"A product with barcode '{bc}' already exists.");

        product.UpdateDetails(name, bc, unit, taxClass);
        if (isActive) product.Reactivate(); else product.Deactivate();
        await _uow.SaveChangesAsync(ct);
        return product;
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
