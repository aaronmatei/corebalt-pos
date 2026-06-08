using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Pos.Api.Contracts;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Xunit;

namespace Pos.Api.Tests;

[Collection(PosApiCollection.Name)]
public sealed class CustomerTests(PosApiFixture fx)
{
    [Fact]
    public async Task Create_search_and_phone_lookup_round_trip()
    {
        var (client, _, _, _) = fx.NewClient();
        var phone = "0712345678";

        var created = await Create(client, "Jane Wanjiku", phone, kraPin: "A001234567Z");
        created.Name.Should().Be("Jane Wanjiku");
        created.Phone.Should().Be("254712345678", "Kenyan numbers are normalized to 2547########");
        created.LoyaltyPoints.Should().Be(0);

        // Search by partial name.
        var search = await client.GetFromJsonAsync<List<CustomerResponse>>("/api/v1/customers?q=wanjiku", PosApiFixture.Json);
        search!.Should().ContainSingle(c => c.Id == created.Id);

        // Exact phone lookup (the till's attach path) — accepts the local 07.. form.
        var byPhone = await client.GetAsync("/api/v1/customers/by-phone/0712345678");
        byPhone.StatusCode.Should().Be(HttpStatusCode.OK);
        (await byPhone.Content.ReadFromJsonAsync<CustomerResponse>(PosApiFixture.Json))!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task Duplicate_phone_is_409_and_bad_kra_pin_is_400()
    {
        var (client, _, _, _) = fx.NewClient();
        await Create(client, "First", "0720000001");

        var dup = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest("Second", "0720000001"), PosApiFixture.Json);
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var badPin = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest("Bad", "0720000002", KraPin: "NOTAPIN"), PosApiFixture.Json);
        badPin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Checkout_attributed_to_a_customer_accrues_loyalty_points()
    {
        var (client, _, _, _) = fx.NewClient();
        var customer = await Create(client, "Loyal Larry", "0733000111");

        // Price 250 (VAT-inclusive). Default rule: 1 point / 100 spent -> floor(250/100) = 2 points.
        var product = await CreateProduct(client, $"LOY-{Guid.NewGuid():N}"[..14], 250m);
        var register = await client.OpenShiftAsync();
        var checkout = new CheckoutRequest(
            RegisterId: register,
            Lines: [new CheckoutLineRequest(product.Id, 1)],
            Tenders: [new CheckoutTenderRequest(TenderType.Cash, 250m)],
            Currency: "KES",
            SaleId: Guid.CreateVersion7(),
            CustomerId: customer.Id);
        (await client.PostAsJsonAsync("/api/v1/sales/checkout", checkout, PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await client.GetFromJsonAsync<CustomerResponse>($"/api/v1/customers/{customer.Id}", PosApiFixture.Json);
        after!.LoyaltyPoints.Should().Be(2);

        // Manager manual adjustment stacks on the accrued balance and never goes negative.
        var adjusted = await client.PostAsJsonAsync($"/api/v1/customers/{customer.Id}/points",
            new AdjustPointsRequest(-5), PosApiFixture.Json);
        (await adjusted.Content.ReadFromJsonAsync<CustomerResponse>(PosApiFixture.Json))!.LoyaltyPoints.Should().Be(0);
    }

    private static async Task<CustomerResponse> Create(HttpClient client, string name, string? phone, string? kraPin = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(name, phone, KraPin: kraPin), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CustomerResponse>(PosApiFixture.Json))!;
    }

    private static async Task<ProductResponse> CreateProduct(HttpClient client, string sku, decimal price)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            Sku: sku, Name: $"Test {sku}", PriceAmount: price, PriceCurrency: "KES",
            UnitOfMeasure: UnitOfMeasure.Each), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }
}
