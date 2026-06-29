using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Identity;
using Pos.Domain.Catalog;
using Pos.SharedKernel;

namespace Pos.Application.Sync;

/// <summary>Port: fetches a page of HQ catalogue changes from the cloud.</summary>
public interface ICatalogPullClient
{
    Task<CatalogPullResponse> PullAsync(long since, int max, CancellationToken ct = default);
}

/// <summary>
/// On-prem store→cloud CATALOGUE pull (M2, the reverse of the sales push). Each pass: read this store's
/// cursor, fetch changes since it, upsert the local store-scoped <see cref="Product"/> by SKU (HQ wins for
/// name/price/tax/active; stock untouched), advance the cursor — all in one transaction. Idempotent: a
/// re-pull from the same cursor just re-applies the same upserts. Reuses the HqSync credentials.
/// </summary>
public sealed class HqCatalogPuller
{
    private readonly ICatalogPullClient _client;
    private readonly ICatalogPullStateRepository _state;
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly StoreServerOptions _server;
    private readonly HqSyncOptions _options;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public HqCatalogPuller(ICatalogPullClient client, ICatalogPullStateRepository state,
        IProductRepository products, ICategoryRepository categories, StoreServerOptions server,
        HqSyncOptions options, IClock clock, IUnitOfWork uow)
    {
        _client = client;
        _state = state;
        _products = products;
        _categories = categories;
        _server = server;
        _options = options;
        _clock = clock;
        _uow = uow;
    }

    /// <summary>Returns the number of catalogue items applied this pass.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled) return 0;
        var tenant = _server.TenantId;
        var store = _server.StoreId;
        if (tenant == Guid.Empty || store == Guid.Empty) return 0;

        var cursor = await _state.GetAsync(tenant, store, ct);
        if (cursor is null)
        {
            cursor = CatalogPullState.Start(tenant, store);
            await _state.AddAsync(cursor, ct);
        }

        var page = await _client.PullAsync(cursor.LastSeq, _options.BatchSize, ct);
        if (page.Items.Count == 0) return 0;

        // Resolve pushed category NAMES to this store's local category ids (categories are tenant-level master
        // data, but their ids are NOT shared across the cloud + on-prem DBs — so we match/create by name). Built
        // once per pass from the existing categories, and grows as we materialize new ones for this batch.
        var categoriesByName = (await _categories.ListAsync(tenant, includeInactive: true, ct))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var applied = 0;
        foreach (var item in page.Items)
        {
            var unit = Enum.TryParse<UnitOfMeasure>(item.UnitOfMeasure, out var u) ? u : UnitOfMeasure.Each;
            var tax = Enum.TryParse<TaxClass>(item.TaxClass, out var t) ? t : TaxClass.StandardRated;
            var price = new Money(item.PriceAmount, item.Currency);
            var categoryId = await ResolveCategoryAsync(tenant, item.CategoryName, categoriesByName, ct);

            var existing = await _products.FindBySkuAsync(tenant, store, item.Sku, ct);
            if (existing is null)
            {
                var product = Product.Create(tenant, store, item.Sku, item.Name, price, unit, item.Barcode, tax, categoryId);
                if (!item.IsActive) product.ApplyHqCatalog(item.Name, item.Barcode, unit, tax, price, active: false, categoryId);
                await _products.AddAsync(product, ct);
            }
            else
            {
                existing.ApplyHqCatalog(item.Name, item.Barcode, unit, tax, price, item.IsActive, categoryId);
            }
            applied++;
        }

        cursor.Advance(page.Cursor, _clock.UtcNow);
        await _uow.SaveChangesAsync(ct);
        return applied;
    }

    /// <summary>Map a pushed category name to a local category id, creating the local category on first sight.
    /// Blank → null (Uncategorized). The cache is shared across the batch so a name creates at most one row.</summary>
    private async Task<Guid?> ResolveCategoryAsync(Guid tenant, string? name,
        Dictionary<string, Guid> cache, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = name.Trim();
        if (cache.TryGetValue(key, out var id)) return id;
        var category = Category.Create(tenant, key);
        await _categories.AddAsync(category, ct);
        cache[key] = category.Id;
        return category.Id;
    }
}
