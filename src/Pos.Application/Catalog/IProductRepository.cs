using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

public interface IProductRepository
{
    Task<Product?> GetAsync(Guid tenantId, Guid storeId, Guid productId, CancellationToken ct = default);
    Task<Product?> FindBySkuAsync(Guid tenantId, Guid storeId, string sku, CancellationToken ct = default);

    /// <summary>Look a product up by its printed scan code (GTIN/EAN-13/UPC) within a store.</summary>
    Task<Product?> FindByBarcodeAsync(Guid tenantId, Guid storeId, string barcode, CancellationToken ct = default);

    /// <summary>
    /// Products in a store, ordered by name. Active only by default; pass includeInactive to also
    /// return soft-deleted ones (back-office views).
    /// </summary>
    Task<IReadOnlyList<Product>> ListAsync(Guid tenantId, Guid storeId, bool includeInactive = false, CancellationToken ct = default);

    Task AddAsync(Product product, CancellationToken ct = default);
}
