using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pos.Api.Contracts;
using Pos.Application.Notifications;
using Pos.Domain.Catalog;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// The real-time push wiring: when the in-app channel writes a low-stock notification to the feed, it also
/// broadcasts it (live, via SignalR in production) to the owning (tenant, store). Here the broadcaster is
/// a recording test double — we assert it received exactly one push carrying the title + unread count.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class RealtimeNotificationTests(PosApiFixture fx)
{
    [Fact]
    public async Task Dispatching_a_low_stock_alert_broadcasts_it_to_the_store()
    {
        fx.Broadcaster.Clear(); // sequential collection — isolate this test's pushes
        var (client, tenant, store, _) = fx.NewClient();
        var register = Uuid7.NewGuid();

        await OpenSession(client, register);
        var product = await CreateProduct(client);
        await Receive(client, product.Id, 10m);
        await SetReorder(client, product, level: 5m);
        await Sell(client, register, product.Id, qty: 6m, cash: 600m); // 10 → 4: dips below the reorder level

        // The dispatcher turns the outbox event into a feed item AND a real-time push.
        using (var scope = fx.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<INotificationDispatcher>().RunOnceAsync();

        var push = fx.Broadcaster.Pushes.Should()
            .ContainSingle(p => p.TenantId == tenant && p.StoreId == store).Subject;
        push.Message.Title.Should().Contain("Low stock");
        push.Message.UnreadCount.Should().Be(1, "one unread alert is now in this store's feed");

        // Re-dispatch is idempotent — no duplicate broadcast.
        using (var scope = fx.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<INotificationDispatcher>().RunOnceAsync();
        fx.Broadcaster.Pushes.Count(p => p.TenantId == tenant).Should().Be(1);
    }

    private static async Task<ProductResponse> CreateProduct(HttpClient client)
    {
        var sku = $"RT-{Guid.NewGuid():N}"[..12];
        var r = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            sku, $"Item {sku}", 100m, "KES", UnitOfMeasure.Each), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }

    private static async Task SetReorder(HttpClient client, ProductResponse p, decimal level)
    {
        var r = await client.PutAsJsonAsync($"/api/v1/products/{p.Id}", new UpdateProductRequest(
            p.Name, p.Barcode, p.UnitOfMeasure, p.TaxClass, IsActive: true,
            CategoryId: p.CategoryId, ReorderLevel: level, ReorderQuantity: null), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
    }

    private static async Task Receive(HttpClient client, Guid productId, decimal qty) =>
        (await client.PostAsJsonAsync("/api/v1/inventory/receive",
            new ReceiveStockRequest(productId, qty, StockMovementReason.OpeningBalance), PosApiFixture.Json))
            .EnsureSuccessStatusCode();

    private static async Task Sell(HttpClient client, Guid register, Guid productId, decimal qty, decimal cash) =>
        (await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: register,
            Lines: [new CheckoutLineRequest(productId, qty)],
            Tenders: [new CheckoutTenderRequest(TenderType.Cash, cash, null)]), PosApiFixture.Json))
            .EnsureSuccessStatusCode();

    private static async Task OpenSession(HttpClient client, Guid register) =>
        (await client.PostAsJsonAsync("/api/v1/sessions/open", new OpenSessionRequest(register, 0m), PosApiFixture.Json))
            .EnsureSuccessStatusCode();
}
