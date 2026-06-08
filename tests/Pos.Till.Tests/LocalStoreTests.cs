using FluentAssertions;
using Pos.Till.Api;
using Pos.Till.Services.Local;
using Xunit;

namespace Pos.Till.Tests;

/// <summary>
/// The till's offline safety net. These cover the two guarantees the offline-first flow depends on:
/// the cached catalogue round-trips (so the till can sell while disconnected) and the sale queue is
/// idempotent on the edge-generated id (so a replayed/duplicated sale is never queued twice).
/// </summary>
public sealed class LocalStoreTests
{
    // An in-memory db lives for the single open connection LocalStore holds — perfect per-test isolation.
    private static async Task<LocalStore> NewStoreAsync()
    {
        var store = new LocalStore(":memory:");
        await store.InitializeAsync();
        return store;
    }

    private static ProductDto Product(string sku, string name, string? barcode, decimal price = 100m) =>
        new(Guid.CreateVersion7(), sku, name, new MoneyDto(price, "KES"),
            UnitOfMeasure.Each, IsActive: true, Barcode: barcode, CategoryId: null);

    [Fact]
    public async Task Cached_catalogue_round_trips_and_is_barcode_searchable()
    {
        await using var store = await NewStoreAsync();
        var milk = Product("MILK-500", "Milk 500ml", "6161100000017");
        var bread = Product("BREAD-400", "Bread 400g", barcode: null);

        await store.CacheProductsAsync([milk, bread]);

        (await store.GetCachedProductsAsync()).Should().HaveCount(2);
        var found = await store.FindByBarcodeAsync("6161100000017");
        found.Should().NotBeNull();
        found!.Sku.Should().Be("MILK-500");
        found.Price.Amount.Should().Be(100m);

        (await store.FindByBarcodeAsync("does-not-exist")).Should().BeNull();
    }

    [Fact]
    public async Task Caching_replaces_the_previous_catalogue()
    {
        await using var store = await NewStoreAsync();
        await store.CacheProductsAsync([Product("A", "Old", "111")]);
        await store.CacheProductsAsync([Product("B", "New", "222")]);

        var products = await store.GetCachedProductsAsync();
        products.Should().ContainSingle().Which.Sku.Should().Be("B");
        (await store.FindByBarcodeAsync("111")).Should().BeNull("the old catalogue was replaced");
    }

    [Fact]
    public async Task Enqueue_is_idempotent_on_the_sale_id()
    {
        await using var store = await NewStoreAsync();
        var saleId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        // Re-queuing the SAME sale id (e.g. a double-tap or a retry) must not create a second entry.
        await store.EnqueueSaleAsync(saleId, """{"first":true}""", now);
        await store.EnqueueSaleAsync(saleId, """{"second":true}""", now);

        (await store.QueuedCountAsync()).Should().Be(1);
        var queued = await store.GetQueuedSalesAsync();
        queued.Should().ContainSingle();
        queued[0].Payload.Should().Contain("first", "INSERT OR IGNORE keeps the first write");
    }

    [Fact]
    public async Task Queue_drains_oldest_first_and_removes_drained_sales()
    {
        await using var store = await NewStoreAsync();
        var older = Guid.CreateVersion7();
        var newer = Guid.CreateVersion7();
        await store.EnqueueSaleAsync(older, "1", DateTimeOffset.UtcNow.AddMinutes(-5));
        await store.EnqueueSaleAsync(newer, "2", DateTimeOffset.UtcNow);

        var queued = await store.GetQueuedSalesAsync();
        queued.Select(q => q.SaleId).Should().ContainInOrder(older, newer);

        await store.RemoveQueuedSaleAsync(older);
        (await store.QueuedCountAsync()).Should().Be(1);
        (await store.GetQueuedSalesAsync()).Single().SaleId.Should().Be(newer);
    }
}
