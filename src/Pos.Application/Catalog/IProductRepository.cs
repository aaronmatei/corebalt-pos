using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

public interface IProductRepository
{
    Task<Product?> GetAsync(Guid tenantId, Guid storeId, Guid productId, CancellationToken ct = default);
    Task<Product?> FindBySkuAsync(Guid tenantId, Guid storeId, string sku, CancellationToken ct = default);

    /// <summary>Look a product up by its printed scan code (GTIN/EAN-13/UPC) within a store.</summary>
    Task<Product?> FindByBarcodeAsync(Guid tenantId, Guid storeId, string barcode, CancellationToken ct = default);

    /// <summary>All products in a store, ordered by name — the till's browsable catalog.</summary>
    Task<IReadOnlyList<Product>> ListAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);

    Task AddAsync(Product product, CancellationToken ct = default);
}
