using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Pos.Api.Contracts;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

[Collection(PosApiCollection.Name)]
public sealed class CheckoutFlowTests(PosApiFixture fx)
{
    [Fact]
    public async Task Full_checkout_round_trips_over_http_and_drains_one_outbox_row()
    {
        var (client, _, _, _) = fx.NewClient();
        var sku = $"TEST-{Guid.NewGuid():N}"[..16];

        // ── Create the product
        var createResp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            Sku: sku, Name: "Test Milk 500ml",
            PriceAmount: 60m, PriceCurrency: "KES",
            UnitOfMeasure: UnitOfMeasure.Each), PosApiFixture.Json);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = (await createResp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
        product.Sku.Should().Be(sku);
        product.Price.Amount.Should().Be(60m);

        // ── Start a sale
        var startResp = await client.PostAsJsonAsync("/api/v1/sales",
            new StartSaleRequest(RegisterId: Uuid7.NewGuid()), PosApiFixture.Json);
        startResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var started = (await startResp.Content.ReadFromJsonAsync<StartSaleResponse>(PosApiFixture.Json))!;

        // ── Add a line and a tender
        var lineResp = await client.PostAsJsonAsync(
            $"/api/v1/sales/{started.SaleId}/lines",
            new AddLineRequest(ProductId: product.Id, Quantity: 2), PosApiFixture.Json);
        lineResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var tenderResp = await client.PostAsJsonAsync(
            $"/api/v1/sales/{started.SaleId}/tenders",
            new AddTenderRequest(Type: TenderType.Cash, Amount: 120m), PosApiFixture.Json);
        tenderResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ── Complete
        var completeResp = await client.PostAsync($"/api/v1/sales/{started.SaleId}/complete", content: null);
        completeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var completed = (await completeResp.Content.ReadFromJsonAsync<CompleteSaleResponse>(PosApiFixture.Json))!;
        completed.Total.Should().Be(120m);
        completed.ChangeDue.Should().Be(0m);
        completed.Currency.Should().Be("KES");

        // ── GET the sale — proves the EF mapping round-trips through HTTP
        var getResp = await client.GetAsync($"/api/v1/sales/{started.SaleId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var sale = (await getResp.Content.ReadFromJsonAsync<SaleResponse>(PosApiFixture.Json))!;
        sale.Status.Should().Be(SaleStatus.Completed);
        sale.Lines.Should().ContainSingle(l => l.ProductId == product.Id && l.Quantity == 2m);
        sale.Tenders.Should().ContainSingle(t => t.Type == TenderType.Cash && t.Amount.Amount == 120m);
        sale.Subtotal.Amount.Should().Be(120m);
        sale.BalanceDue.Amount.Should().Be(0m);

        // ── Stock-on-hand reflects the negative-delta movement written by CompleteAsync
        var onHandResp = await client.GetAsync($"/api/v1/inventory/{product.Id}/on-hand");
        onHandResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var onHand = (await onHandResp.Content.ReadFromJsonAsync<StockOnHandResponse>(PosApiFixture.Json))!;
        onHand.OnHand.Should().Be(-2m,
            "INVARIANT #3 — on-hand is the SUM of immutable movements; the sale produced exactly one -2 movement");
    }

    [Fact]
    public async Task Cannot_complete_an_underpaid_sale_returns_409()
    {
        var (client, _, _, _) = fx.NewClient();
        var sku = $"TEST-{Guid.NewGuid():N}"[..16];

        var product = await CreateProduct(client, sku, 100m);
        var saleId  = await StartSale(client);

        await AddLine(client, saleId, product.Id, 1);
        await AddTender(client, saleId, TenderType.Cash, 30m);

        var resp = await client.PostAsync($"/api/v1/sales/{saleId}/complete", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "domain rule 'sale not fully paid' maps to 409 via DomainExceptionHandler");
    }

    [Fact]
    public async Task Reprice_updates_the_stored_price()
    {
        var (client, _, _, _) = fx.NewClient();
        var sku = $"TEST-{Guid.NewGuid():N}"[..16];

        var product = await CreateProduct(client, sku, 50m);
        var repriceResp = await client.PutAsJsonAsync(
            $"/api/v1/products/{product.Id}/price",
            new RepriceProductRequest(Amount: 75m, Currency: "KES"), PosApiFixture.Json);
        repriceResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResp = await client.GetAsync($"/api/v1/products/{product.Id}");
        var reloaded = (await getResp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
        reloaded.Price.Amount.Should().Be(75m);
    }

    // ── tiny helpers
    private static async Task<ProductResponse> CreateProduct(HttpClient client, string sku, decimal price)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            Sku: sku, Name: $"Test {sku}",
            PriceAmount: price, PriceCurrency: "KES",
            UnitOfMeasure: UnitOfMeasure.Each), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }

    private static async Task<Guid> StartSale(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/sales",
            new StartSaleRequest(RegisterId: Uuid7.NewGuid()), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<StartSaleResponse>(PosApiFixture.Json))!.SaleId;
    }

    private static async Task AddLine(HttpClient client, Guid saleId, Guid productId, decimal qty)
    {
        var resp = await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/lines",
            new AddLineRequest(productId, qty), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task AddTender(HttpClient client, Guid saleId, TenderType type, decimal amount)
    {
        var resp = await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/tenders",
            new AddTenderRequest(type, amount), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
    }
}
