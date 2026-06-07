using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;
using Pos.Api.Contracts;
using Pos.Domain.Catalog;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

[Collection(PosApiCollection.Name)]
public sealed class StockReceivingTests(PosApiFixture fx)
{
    [Fact]
    public async Task Receive_then_sell_leaves_on_hand_derived_from_movements()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 50m);

        var received = await Receive(client, product.Id, 10m, StockMovementReason.Purchase, "GRN-001");
        received.OnHand.Should().Be(10m);

        // Sell 3 (a normal cash checkout writes a -3 Sale movement).
        var register = await client.OpenShiftAsync();
        var checkout = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: register,
            Lines: new[] { new CheckoutLineRequest(product.Id, 3m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 200m, null) }), PosApiFixture.Json);
        checkout.EnsureSuccessStatusCode();

        (await OnHand(client, product.Id)).Should().Be(7m, "10 received - 3 sold, summed from the ledger");
    }

    [Fact]
    public async Task Adjustment_shifts_on_hand_up_and_down()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 50m);
        await Receive(client, product.Id, 10m, StockMovementReason.Purchase, null);

        (await Adjust(client, product.Id, -2m, "stock take shrinkage")).OnHand.Should().Be(8m);
        (await Adjust(client, product.Id, +5m, "found stock")).OnHand.Should().Be(13m);
    }

    [Fact]
    public async Task Receive_rejects_non_positive_quantity()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 50m);
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/receive",
            new ReceiveStockRequest(product.Id, 0m, StockMovementReason.Purchase), PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stock_report_lists_products_with_derived_on_hand()
    {
        var (client, _, _, _) = fx.NewClient();
        var a = await CreateProduct(client, 50m);
        var b = await CreateProduct(client, 50m);
        await Receive(client, a.Id, 4m, StockMovementReason.OpeningBalance, null);
        await Adjust(client, b.Id, -1m, null);

        var report = (await (await client.GetAsync("/api/v1/inventory/report"))
            .Content.ReadFromJsonAsync<StockReportResponse>(PosApiFixture.Json))!;
        report.Items.Single(r => r.ProductId == a.Id).OnHand.Should().Be(4m);
        report.Items.Single(r => r.ProductId == b.Id).OnHand.Should().Be(-1m);
    }

    [Fact]
    public async Task On_hand_is_never_a_persisted_column()
    {
        // Stock-on-hand is always SUM(movements); neither products nor any table stores a mutable count.
        await using var conn = new NpgsqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'products'
              AND (column_name LIKE '%on_hand%' OR column_name LIKE '%onhand%'
                   OR column_name = 'quantity' OR column_name = 'stock' OR column_name = 'qty')";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().Be(0, "products must not carry a stored stock count");
    }

    // ── helpers ──
    private static async Task<StockMovementResponse> Receive(HttpClient client, Guid productId, decimal qty, StockMovementReason reason, string? reference)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/receive",
            new ReceiveStockRequest(productId, qty, reason, reference), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<StockMovementResponse>(PosApiFixture.Json))!;
    }

    private static async Task<StockMovementResponse> Adjust(HttpClient client, Guid productId, decimal qty, string? reference)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/adjust",
            new AdjustStockRequest(productId, qty, reference), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<StockMovementResponse>(PosApiFixture.Json))!;
    }

    private static async Task<decimal> OnHand(HttpClient client, Guid productId) =>
        (await (await client.GetAsync($"/api/v1/inventory/{productId}/on-hand"))
            .Content.ReadFromJsonAsync<StockOnHandResponse>(PosApiFixture.Json))!.OnHand;

    private static async Task<ProductResponse> CreateProduct(HttpClient client, decimal price)
    {
        var sku = $"ST-{Guid.NewGuid():N}"[..12];
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            Sku: sku, Name: $"Item {sku}", PriceAmount: price, PriceCurrency: "KES",
            UnitOfMeasure: UnitOfMeasure.Each, Barcode: null, TaxClass: TaxClass.StandardRated), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }
}
