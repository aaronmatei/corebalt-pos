using Microsoft.EntityFrameworkCore;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class ProductRepository : IProductRepository
{
    private readonly PosDbContext _db;
    public ProductRepository(PosDbContext db) => _db = db;

    public Task<Product?> GetAsync(Guid tenantId, Guid storeId, Guid productId, CancellationToken ct = default) =>
        _db.Products
            .Where(p => p.TenantId == tenantId && p.StoreId == storeId && p.Id == productId)
            .FirstOrDefaultAsync(ct);

    public Task<Product?> FindBySkuAsync(Guid tenantId, Guid storeId, string sku, CancellationToken ct = default) =>
        _db.Products
            .Where(p => p.TenantId == tenantId && p.StoreId == storeId && p.Sku == sku)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(Product product, CancellationToken ct = default) =>
        await _db.Products.AddAsync(product, ct);
}
