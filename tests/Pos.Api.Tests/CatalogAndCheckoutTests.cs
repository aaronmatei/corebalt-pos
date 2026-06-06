using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Pos.Api.Contracts;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Covers the till-facing additions: a barcode (GTIN/EAN-13) lookup distinct from the SKU,
/// the products list, and the one-shot atomic /sales/checkout endpoint. Each test mints a
/// fresh tenant/store via NewClient() so the list assertions stay isolated.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class CatalogAndCheckoutTests(PosApiFixture fx)
{
    [Fact]
    public async Task Product_can_be_found_by_its_barcode_distinct_from_sku()
    {
        var (client, _, _, _) = fx.NewClient();
        var sku = $"SKU-{Guid.NewGuid():N}"[..12];
        var barcode = NewEan13();

        var created = await CreateProduct(client, sku, "Tinned Beans 400g", 95m, barcode: barcode);
        created.Barcode.Should().Be(barcode);

        // by-barcode resolves to the same product
        var byBarcode = await client.GetAsync($"/api/v1/products/barcode/{barcode}");
        byBarcode.StatusCode.Should().Be(HttpStatusCode.OK);
        var found = (await byBarcode.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
        found.Id.Should().Be(created.Id);

        // the barcode is NOT the sku — looking the barcode up as a sku misses
        var asSku = await client.GetAsync($"/api/v1/products/by-sku/{barcode}");
        asSku.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // unknown barcode → 404
        var unknown = await client.GetAsync($"/api/v1/products/barcode/{NewEan13()}");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Products_list_returns_the_stores_catalog()
    {
        var (client, _, _, _) = fx.NewClient(); // fresh tenant/store → list is just what we add

        var a = await CreateProduct(client, $"A-{Guid.NewGuid():N}"[..10], "Apples 1kg", 120m, UnitOfMeasure.Kg);
        var b = await CreateProduct(client, $"B-{Guid.NewGuid():N}"[..10], "Bread 400g", 55m);

        var resp = await client.GetAsync("/api/v1/products");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = (await resp.Content.ReadFromJsonAsync<List<ProductResponse>>(PosApiFixture.Json))!;

        list.Should().HaveCount(2);
        list.Select(p => p.Id).Should().BeEquivalentTo(new[] { a.Id, b.Id });
        list.Should().ContainSingle(p => p.UnitOfMeasure == UnitOfMeasure.Kg && p.Name == "Apples 1kg");
    }

    [Fact]
    public async Task Atomic_checkout_completes_a_cash_sale_and_returns_change()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, $"C-{Guid.NewGuid():N}"[..10], "Sugar 2kg", 250m);

        var req = new CheckoutRequest(
            RegisterId: Uuid7.NewGuid(),
            Lines: new[] { new CheckoutLineRequest(product.Id, 2m) },     // 2 × 250 = 500
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 600m, null) });

        var resp = await client.PostAsJsonAsync("/api/v1/sales/checkout", req, PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await resp.Content.ReadFromJsonAsync<CompleteSaleResponse>(PosApiFixture.Json))!;
        result.Total.Should().Be(500m);
        result.ChangeDue.Should().Be(100m);
        result.Currency.Should().Be("KES");

        // The sale is persisted and completed
        var sale = (await (await client.GetAsync($"/api/v1/sales/{result.SaleId}"))
            .Content.ReadFromJsonAsync<SaleResponse>(PosApiFixture.Json))!;
        sale.Status.Should().Be(SaleStatus.Completed);
        sale.Subtotal.Amount.Should().Be(500m);

        // Stock-on-hand reflects the single -2 movement written in the same transaction
        var onHand = (await (await client.GetAsync($"/api/v1/inventory/{product.Id}/on-hand"))
            .Content.ReadFromJsonAsync<StockOnHandResponse>(PosApiFixture.Json))!;
        onHand.OnHand.Should().Be(-2m);
    }

    [Fact]
    public async Task Atomic_checkout_with_an_mpesa_reference_records_the_tender()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, $"M-{Guid.NewGuid():N}"[..10], "Cooking Oil 1L", 300m);

        var req = new CheckoutRequest(
            RegisterId: Uuid7.NewGuid(),
            Lines: new[] { new CheckoutLineRequest(product.Id, 1m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Mpesa, 300m, "QABC123XYZ") });

        var resp = await client.PostAsJsonAsync("/api/v1/sales/checkout", req, PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await resp.Content.ReadFromJsonAsync<CompleteSaleResponse>(PosApiFixture.Json))!;
        result.ChangeDue.Should().Be(0m);

        var sale = (await (await client.GetAsync($"/api/v1/sales/{result.SaleId}"))
            .Content.ReadFromJsonAsync<SaleResponse>(PosApiFixture.Json))!;
        sale.Tenders.Should().ContainSingle(t => t.Type == TenderType.Mpesa && t.Reference == "QABC123XYZ");
    }

    [Fact]
    public async Task Atomic_checkout_underpaid_returns_409_and_persists_nothing()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, $"U-{Guid.NewGuid():N}"[..10], "Rice 5kg", 700m);

        var req = new CheckoutRequest(
            RegisterId: Uuid7.NewGuid(),
            Lines: new[] { new CheckoutLineRequest(product.Id, 1m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 100m, null) }); // underpaid

        var resp = await client.PostAsJsonAsync("/api/v1/sales/checkout", req, PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "'sale not fully paid' is a domain rule → 409; the atomic transaction rolls back");

        // Nothing committed → no stock movement
        var onHand = (await (await client.GetAsync($"/api/v1/inventory/{product.Id}/on-hand"))
            .Content.ReadFromJsonAsync<StockOnHandResponse>(PosApiFixture.Json))!;
        onHand.OnHand.Should().Be(0m);
    }

    [Fact]
    public async Task Atomic_checkout_with_no_lines_returns_400()
    {
        var (client, _, _, _) = fx.NewClient();
        var req = new CheckoutRequest(
            RegisterId: Uuid7.NewGuid(),
            Lines: Array.Empty<CheckoutLineRequest>(),
            Tenders: Array.Empty<CheckoutTenderRequest>());

        var resp = await client.PostAsJsonAsync("/api/v1/sales/checkout", req, PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an empty basket is an argument error → 400");
    }

    // ── helpers
    private static async Task<ProductResponse> CreateProduct(
        HttpClient client, string sku, string name, decimal price,
        UnitOfMeasure unit = UnitOfMeasure.Each, string? barcode = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            Sku: sku, Name: name, PriceAmount: price, PriceCurrency: "KES",
            UnitOfMeasure: unit, Barcode: barcode), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }

    // A plausible 13-digit code. Distinct from any SKU we generate (which carry letter prefixes).
    private static string NewEan13() => string.Concat("20", $"{Math.Abs(Guid.NewGuid().GetHashCode())}".PadLeft(11, '0').AsSpan(0, 11));
}
