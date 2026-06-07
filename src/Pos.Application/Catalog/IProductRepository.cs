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
    /// return soft-deleted ones (back-office views). Pass categoryId to narrow to one category; the
    /// sentinel <see cref="Uncategorized"/> returns products with no category.
    /// </summary>
    Task<IReadOnlyList<Product>> ListAsync(Guid tenantId, Guid storeId, bool includeInactive = false,
        Guid? categoryId = null, CancellationToken ct = default);

    /// <summary>Filter sentinel for <see cref="ListAsync"/>: products whose CategoryId is null.</summary>
    public static readonly Guid Uncategorized = Guid.Empty;

    Task AddAsync(Product product, CancellationToken ct = default);

    /// <summary>
    /// Current CategoryId for each of the given products (productId → CategoryId, null if uncategorized
    /// or unknown). Used by sales-by-category reporting, which joins sale lines to the product's CURRENT
    /// category (v1). Only ids that exist in the tenant are returned.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Guid?>> GetCategoryMapAsync(Guid tenantId, IReadOnlyCollection<Guid> productIds, CancellationToken ct = default);

    /// <summary>Does any product in the TENANT already use this SKU? (excludingProductId skips the row being updated.)</summary>
    Task<bool> SkuExistsAsync(Guid tenantId, string sku, Guid? excludingProductId = null, CancellationToken ct = default);

    /// <summary>Does any product in the TENANT already use this barcode?</summary>
    Task<bool> BarcodeExistsAsync(Guid tenantId, string barcode, Guid? excludingProductId = null, CancellationToken ct = default);
}
