using System.Net.Http.Json;
using FluentAssertions;
using Pos.Api.Contracts;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// The receipt shows a human lane label ("Lane 1") for the register, not the RegisterId GUID — captured
/// at sale time so reprints stay identical.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class RegisterLabelTests(PosApiFixture fx)
{
    [Fact]
    public async Task Receipt_shows_the_human_register_label_not_the_guid_and_reprints_identically()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);
        var registerId = Uuid7.NewGuid();
        var saleId = await Checkout(client, registerId, product.Id);

        var receipt = await GetReceipt(client, saleId);

        receipt.Model.Meta.Register.Should().Be("Lane 1");                 // first lane in this store
        receipt.Model.Meta.Register.Should().NotContain(registerId.ToString("N")[..8].ToUpperInvariant());
        receipt.Text.Should().Contain("Till: Lane 1");

        // Reprint reads the persisted, captured label → byte-identical.
        var reprint = await GetReceipt(client, saleId);
        reprint.Text.Should().Be(receipt.Text);
    }

    [Fact]
    public async Task A_second_lane_in_the_same_store_gets_the_next_number()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);

        var first = await GetReceipt(client, await Checkout(client, Uuid7.NewGuid(), product.Id));
        var second = await GetReceipt(client, await Checkout(client, Uuid7.NewGuid(), product.Id));

        first.Model.Meta.Register.Should().Be("Lane 1");
        second.Model.Meta.Register.Should().Be("Lane 2");
    }

    // ── helpers ──
    private static async Task<ProductResponse> CreateProduct(HttpClient client, decimal price)
    {
        var sku = $"RL-{Guid.NewGuid():N}"[..12];
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            sku, $"Item {sku}", price, "KES", UnitOfMeasure.Each, null, TaxClass.StandardRated), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }

    private static async Task<Guid> Checkout(HttpClient client, Guid registerId, Guid productId)
    {
        await client.OpenShiftAsync(registerId);
        var resp = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: registerId,
            Lines: new[] { new CheckoutLineRequest(productId, 1m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 100m, null) }), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CompleteSaleResponse>(PosApiFixture.Json))!.SaleId;
    }

    private static async Task<ReceiptResponse> GetReceipt(HttpClient client, Guid saleId) =>
        (await (await client.GetAsync($"/api/v1/sales/{saleId}/receipt"))
            .Content.ReadFromJsonAsync<ReceiptResponse>(PosApiFixture.Json))!;
}
