using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Api.Contracts;
using Pos.Application.Notifications;
using Pos.Domain.Catalog;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;
using Pos.Infrastructure.Persistence;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Reorder-level (low-stock) alerts. On-hand is SUM(movements); "low" is DERIVED (never a flag). The
/// ProductLowStock event fires ONCE per dip (on the downward crossing), clears when stock is lifted back
/// above the level, and re-arms for the next dip. Products without a reorder level are never flagged.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class LowStockTests(PosApiFixture fx)
{
    [Fact]
    public async Task Selling_to_the_reorder_level_emits_one_event_and_shows_in_report_badge_and_feed()
    {
        var (client, tenant, _, _) = fx.NewClient();
        var register = Uuid7.NewGuid();
        await OpenSession(client, register, 0m);

        var product = await CreateProduct(client, price: 100m);
        await Receive(client, product.Id, 10m);                 // on-hand 10
        await SetReorder(client, product, level: 5m, qty: 20m); // track at 5

        await Sell(client, register, product.Id, qty: 5m, cash: 500m); // on-hand 10 → 5 (crosses to the level)

        // Exactly one ProductLowStock event landed in the outbox.
        (await LowStockEventCount(product.Id)).Should().Be(1);

        // Derived report + badge reflect it immediately.
        var report = await GetLowStock(client);
        report.Count.Should().BeGreaterThanOrEqualTo(1);
        var row = report.Items.Should().ContainSingle(r => r.ProductId == product.Id).Subject;
        row.OnHand.Should().Be(5m);
        row.ReorderLevel.Should().Be(5m);
        row.SuggestedOrderQty.Should().Be(20m);

        // The dispatcher turns the outbox event into exactly one feed item — and is idempotent.
        await RunDispatcher();
        await RunDispatcher();
        var feed = await GetNotifications(client);
        feed.UnreadCount.Should().Be(1);
        feed.Items.Should().ContainSingle(n => n.ProductId == product.Id)
            .Which.Title.Should().Contain("Low stock");
    }

    [Fact]
    public async Task Staying_below_does_not_renotify_but_recovering_then_dipping_again_does()
    {
        var (client, _, _, _) = fx.NewClient();
        var register = Uuid7.NewGuid();
        await OpenSession(client, register, 0m);

        var product = await CreateProduct(client, price: 100m);
        await Receive(client, product.Id, 10m);
        await SetReorder(client, product, level: 5m, qty: null);

        await Sell(client, register, product.Id, qty: 5m, cash: 500m);  // 10 → 5: event #1
        (await LowStockEventCount(product.Id)).Should().Be(1);

        await Sell(client, register, product.Id, qty: 2m, cash: 200m);  // 5 → 3: still below, NO new event
        (await LowStockEventCount(product.Id)).Should().Be(1, "staying below must not re-notify");

        await Receive(client, product.Id, 10m);                         // 3 → 13: recovered above, clears the flag
        (await LowStockEventCount(product.Id)).Should().Be(1, "recovering above the level emits nothing");

        await Sell(client, register, product.Id, qty: 9m, cash: 900m);  // 13 → 4: a fresh dip → event #2
        (await LowStockEventCount(product.Id)).Should().Be(2, "a later dip after recovery notifies again");
    }

    [Fact]
    public async Task A_product_with_no_reorder_level_is_never_flagged_and_on_hand_is_pure_movements()
    {
        var (client, _, _, _) = fx.NewClient();
        var register = Uuid7.NewGuid();
        await OpenSession(client, register, 0m);

        var tracked = await CreateProduct(client, price: 100m);
        var untracked = await CreateProduct(client, price: 100m);
        await Receive(client, tracked.Id, 10m);
        await Receive(client, untracked.Id, 1m);                 // genuinely low, but no level set
        await SetReorder(client, tracked, level: 5m, qty: 5m);

        await Sell(client, register, untracked.Id, qty: 1m, cash: 100m); // on-hand 0, still never flagged
        await Sell(client, register, tracked.Id, qty: 6m, cash: 600m);   // 10 → 4: tracked dips

        (await LowStockEventCount(untracked.Id)).Should().Be(0, "no reorder level → never an event");

        var report = await GetLowStock(client);
        report.Items.Should().Contain(r => r.ProductId == tracked.Id);
        report.Items.Should().NotContain(r => r.ProductId == untracked.Id);

        // On-hand on the report is purely SUM(movements): received 10, sold 6 → 4.
        report.Items.Single(r => r.ProductId == tracked.Id).OnHand.Should().Be(4m);
    }

    // ── helpers ──
    private static async Task<ProductResponse> CreateProduct(HttpClient client, decimal price)
    {
        var sku = $"LS-{Guid.NewGuid():N}"[..12];
        var r = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            sku, $"Item {sku}", price, "KES", UnitOfMeasure.Each, null, TaxClass.StandardRated), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }

    private static async Task SetReorder(HttpClient client, ProductResponse p, decimal? level, decimal? qty)
    {
        var r = await client.PutAsJsonAsync($"/api/v1/products/{p.Id}", new UpdateProductRequest(
            p.Name, p.Barcode, p.UnitOfMeasure, p.TaxClass, IsActive: true,
            CategoryId: p.CategoryId, ReorderLevel: level, ReorderQuantity: qty), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
    }

    private static async Task Receive(HttpClient client, Guid productId, decimal qty)
    {
        var r = await client.PostAsJsonAsync("/api/v1/inventory/receive",
            new ReceiveStockRequest(productId, qty, StockMovementReason.OpeningBalance), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
    }

    private static async Task Sell(HttpClient client, Guid register, Guid productId, decimal qty, decimal cash)
    {
        var r = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: register,
            Lines: new[] { new CheckoutLineRequest(productId, qty) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, cash, null) }), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
    }

    private static async Task OpenSession(HttpClient client, Guid register, decimal openingFloat)
    {
        var r = await client.PostAsJsonAsync("/api/v1/sessions/open", new OpenSessionRequest(register, openingFloat), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
    }

    private static async Task<LowStockResponse> GetLowStock(HttpClient client) =>
        (await (await client.GetAsync("/api/v1/inventory/low-stock"))
            .Content.ReadFromJsonAsync<LowStockResponse>(PosApiFixture.Json))!;

    private static async Task<NotificationFeedResponse> GetNotifications(HttpClient client) =>
        (await (await client.GetAsync("/api/v1/notifications"))
            .Content.ReadFromJsonAsync<NotificationFeedResponse>(PosApiFixture.Json))!;

    private async Task<int> LowStockEventCount(Guid productId)
    {
        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
        return await db.OutboxMessages.CountAsync(m => m.AggregateId == productId && m.EventType.Contains("ProductLowStock"));
    }

    private async Task RunDispatcher()
    {
        using var scope = fx.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationDispatcher>().RunOnceAsync();
    }
}
