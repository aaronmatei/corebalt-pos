using FluentAssertions;
using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Identity;
using Pos.Application.Sync;
using Pos.Domain.Catalog;
using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// M2 store side: HqCatalogPuller applies a pulled catalogue page to the store's local products — upsert
/// by SKU, HQ-authoritative fields, the cursor advanced, and crucially NO domain events (so applying an
/// HQ push never churns the store outbox or loops back). Pure unit test over the ports.
/// </summary>
public sealed class HqCatalogPullerTests
{
    [Fact]
    public async Task Applies_catalogue_by_sku_upsert_advances_cursor_and_raises_no_events()
    {
        var tenant = Uuid7.NewGuid();
        var store = Uuid7.NewGuid();

        // Existing local product for MILK (old price/name); SUGAR is new.
        var milk = Product.Create(tenant, store, "MILK", "Milk OLD", new Money(50m, "KES"), UnitOfMeasure.Each, null, TaxClass.StandardRated);
        var products = new FakeProducts(milk);

        var page = new CatalogPullResponse(new[]
        {
            new CatalogItemDto(1, Uuid7.NewGuid(), "MILK",  "Milk 500ml", 65m,  "KES", "StandardRated", "Each", null, true),
            new CatalogItemDto(2, Uuid7.NewGuid(), "SUGAR", "Sugar 1kg",  150m, "KES", "StandardRated", "Each", null, true),
        }, Cursor: 2, HasMore: false);

        var state = new FakeState();
        var categories = new FakeCategories();
        var puller = new HqCatalogPuller(new FakeClient(page), state, products, categories,
            new StoreServerOptions { TenantId = tenant, StoreId = store },
            new HqSyncOptions { Enabled = true, TenantSlug = "acme", BatchSize = 100 },
            new FakeClock(), new FakeUow());

        var applied = await puller.RunOnceAsync();

        applied.Should().Be(2);
        milk.Name.Should().Be("Milk 500ml");                 // existing updated in place by SKU…
        milk.Price.Amount.Should().Be(65m);
        milk.DomainEvents.Should().BeEmpty("applying an HQ push must not raise events");
        var sugar = products.Added.Should().ContainSingle(p => p.Sku == "SUGAR").Subject; // …new one created
        sugar.Price.Amount.Should().Be(150m);
        sugar.DomainEvents.Should().BeEmpty();
        state.Current!.LastSeq.Should().Be(2);               // cursor advanced
    }

    [Fact]
    public async Task Pushed_category_name_materializes_a_local_category_and_tags_the_product()
    {
        var tenant = Uuid7.NewGuid();
        var store = Uuid7.NewGuid();
        var products = new FakeProducts(null);
        var categories = new FakeCategories();

        var page = new CatalogPullResponse(new[]
        {
            new CatalogItemDto(1, Uuid7.NewGuid(), "MILK",  "Milk 500ml", 65m, "KES", "StandardRated", "Each", null, true, "Dairy"),
            new CatalogItemDto(2, Uuid7.NewGuid(), "YOGHRT", "Yoghurt",   90m, "KES", "StandardRated", "Each", null, true, "Dairy"),
        }, Cursor: 2, HasMore: false);

        var puller = new HqCatalogPuller(new FakeClient(page), new FakeState(), products, categories,
            new StoreServerOptions { TenantId = tenant, StoreId = store },
            new HqSyncOptions { Enabled = true, TenantSlug = "acme", BatchSize = 100 }, new FakeClock(), new FakeUow());

        await puller.RunOnceAsync();

        // The "Dairy" category is created ONCE and both products point at it.
        var dairy = categories.Added.Should().ContainSingle(c => c.Name == "Dairy").Subject;
        products.Added.Should().OnlyContain(p => p.CategoryId == dairy.Id);
    }

    // ── fakes ──
    private sealed class FakeClient(CatalogPullResponse page) : ICatalogPullClient
    {
        public Task<CatalogPullResponse> PullAsync(long since, int max, CancellationToken ct = default) =>
            Task.FromResult(page);
    }

    private sealed class FakeState : ICatalogPullStateRepository
    {
        public CatalogPullState? Current { get; private set; }
        public Task<CatalogPullState?> GetAsync(Guid t, Guid s, CancellationToken ct = default) => Task.FromResult(Current);
        public Task AddAsync(CatalogPullState state, CancellationToken ct = default) { Current = state; return Task.CompletedTask; }
    }

    private sealed class FakeProducts(Product? existing) : IProductRepository
    {
        public List<Product> Added { get; } = new();
        public Task<Product?> FindBySkuAsync(Guid t, Guid s, string sku, CancellationToken ct = default) =>
            Task.FromResult(existing is not null && existing.Sku == sku ? existing : null);
        public Task AddAsync(Product p, CancellationToken ct = default) { Added.Add(p); return Task.CompletedTask; }
        public Task<Product?> GetAsync(Guid t, Guid s, Guid id, CancellationToken ct = default) => Task.FromResult<Product?>(null);
        public Task<Product?> FindByBarcodeAsync(Guid t, Guid s, string bc, CancellationToken ct = default) => Task.FromResult<Product?>(null);
        public Task<IReadOnlyList<Product>> ListAsync(Guid t, Guid s, bool inc = false, Guid? cat = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Product>>([]);
        public Task<IReadOnlyDictionary<Guid, Guid?>> GetCategoryMapAsync(Guid t, IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Guid?>>(new Dictionary<Guid, Guid?>());
        public Task<bool> SkuExistsAsync(Guid t, string sku, Guid? ex = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> BarcodeExistsAsync(Guid t, string bc, Guid? ex = null, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class FakeCategories : ICategoryRepository
    {
        public List<Category> Added { get; } = new();
        public Task<Category?> GetAsync(Guid t, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Added.FirstOrDefault(c => c.Id == id));
        public Task<IReadOnlyList<Category>> ListAsync(Guid t, bool includeInactive = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Category>>(Added.Where(c => c.TenantId == t).ToList());
        public Task AddAsync(Category category, CancellationToken ct = default) { Added.Add(category); return Task.CompletedTask; }
        public Task<bool> NameExistsAsync(Guid t, Guid? parentId, string name, Guid? excludingCategoryId = null, CancellationToken ct = default) =>
            Task.FromResult(Added.Any(c => c.TenantId == t && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

    private sealed class FakeUow : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) => work(ct);
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default) => work(ct);
    }
}
