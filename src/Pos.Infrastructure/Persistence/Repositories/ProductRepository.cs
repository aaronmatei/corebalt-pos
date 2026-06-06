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

    public Task<Product?> FindByBarcodeAsync(Guid tenantId, Guid storeId, string barcode, CancellationToken ct = default) =>
        _db.Products
            .Where(p => p.TenantId == tenantId && p.StoreId == storeId && p.Barcode == barcode)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Product>> ListAsync(Guid tenantId, Guid storeId, bool includeInactive = false, CancellationToken ct = default) =>
        await _db.Products
            .Where(p => p.TenantId == tenantId && p.StoreId == storeId && (includeInactive || p.IsActive))
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    public async Task AddAsync(Product product, CancellationToken ct = default) =>
        await _db.Products.AddAsync(product, ct);

    public Task<bool> SkuExistsAsync(Guid tenantId, string sku, Guid? excludingProductId = null, CancellationToken ct = default) =>
        _db.Products.AnyAsync(p => p.TenantId == tenantId && p.Sku == sku
            && (excludingProductId == null || p.Id != excludingProductId), ct);

    public Task<bool> BarcodeExistsAsync(Guid tenantId, string barcode, Guid? excludingProductId = null, CancellationToken ct = default) =>
        _db.Products.AnyAsync(p => p.TenantId == tenantId && p.Barcode == barcode
            && (excludingProductId == null || p.Id != excludingProductId), ct);
}
