using System.Net.Http.Json;
using FluentAssertions;
using Pos.Api.Contracts;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// The human-readable receipt number is store-authoritative and monotonic per (tenant, store),
/// separate from the UUIDv7 id, and stable across reprints.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class ReceiptNumberTests(PosApiFixture fx)
{
    [Fact]
    public async Task Two_sales_in_the_same_store_get_consecutive_numbers_stable_across_reprints()
    {
        // Same client → same tenant/store → shares the per-store counter (a fresh store starts at 1).
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);

        var first = await CheckoutAndGetReceipt(client, product.Id);
        var second = await CheckoutAndGetReceipt(client, product.Id);

        // Branch-prefixed format "MB-000001"
        first.Model.Meta.ReceiptNo.Should().MatchRegex(@"^MB-\d{6}$");
        second.Model.Meta.ReceiptNo.Should().MatchRegex(@"^MB-\d{6}$");

        var n1 = SeqOf(first.Model.Meta.ReceiptNo);
        var n2 = SeqOf(second.Model.Meta.ReceiptNo);
        n2.Should().Be(n1 + 1, "receipt numbers are consecutive within a store");

        // The receipt number is distinct from the internal UUIDv7 (kept as Ref for support).
        first.Model.Meta.Ref.Should().NotBe(first.Model.Meta.ReceiptNo);
        Guid.Parse(first.Model.Meta.Ref).Should().NotBe(Guid.Empty);
        first.Text.Should().Contain("Receipt No:").And.Contain(first.Model.Meta.ReceiptNo);

        // Stable across reprints: a second fetch renders the SAME number, byte-identical.
        var reprint = (await (await client.GetAsync($"/api/v1/sales/{first.Model.Meta.Ref}/receipt"))
            .Content.ReadFromJsonAsync<ReceiptResponse>(PosApiFixture.Json))!;
        reprint.Model.Meta.ReceiptNo.Should().Be(first.Model.Meta.ReceiptNo);
        reprint.Text.Should().Be(first.Text);
    }

    [Fact]
    public async Task Different_stores_keep_independent_sequences()
    {
        var (clientA, _, _, _) = fx.NewClient();
        var (clientB, _, _, _) = fx.NewClient();
        var pa = await CreateProduct(clientA, 100m);
        var pb = await CreateProduct(clientB, 100m);

        var a = await CheckoutAndGetReceipt(clientA, pa.Id);
        var b = await CheckoutAndGetReceipt(clientB, pb.Id);

        // Each fresh store starts its own sequence at 1 (store-authoritative, not a global counter).
        SeqOf(a.Model.Meta.ReceiptNo).Should().Be(1);
        SeqOf(b.Model.Meta.ReceiptNo).Should().Be(1);
    }

    private static int SeqOf(string receiptNo) => int.Parse(receiptNo.Split('-')[^1]);

    private static async Task<ReceiptResponse> CheckoutAndGetReceipt(HttpClient client, Guid productId)
    {
        var register = await client.OpenShiftAsync();
        var checkout = (await (await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: register,
            Lines: new[] { new CheckoutLineRequest(productId, 1m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 100m, null) }), PosApiFixture.Json))
            .Content.ReadFromJsonAsync<CompleteSaleResponse>(PosApiFixture.Json))!;

        return (await (await client.GetAsync($"/api/v1/sales/{checkout.SaleId}/receipt"))
            .Content.ReadFromJsonAsync<ReceiptResponse>(PosApiFixture.Json))!;
    }

    private static async Task<ProductResponse> CreateProduct(HttpClient client, decimal price)
    {
        var sku = $"RN-{Guid.NewGuid():N}"[..12];
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            Sku: sku, Name: $"Item {sku}", PriceAmount: price, PriceCurrency: "KES",
            UnitOfMeasure: UnitOfMeasure.Each, Barcode: null, TaxClass: TaxClass.StandardRated), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }
}
