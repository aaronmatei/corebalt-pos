using Pos.Domain.Catalog;

namespace Pos.Application.Catalog;

public interface IProductRepository
{
    Task<Product?> GetAsync(Guid tenantId, Guid storeId, Guid productId, CancellationToken ct = default);
    Task<Product?> FindBySkuAsync(Guid tenantId, Guid storeId, string sku, CancellationToken ct = default);
    Task AddAsync(Product product, CancellationToken ct = default);
}
