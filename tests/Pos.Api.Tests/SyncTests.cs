using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Pos.Api.Contracts;
using Pos.Api.Endpoints;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Xunit;

namespace Pos.Api.Tests;

[Collection(PosApiCollection.Name)]
public sealed class SyncTests(PosApiFixture fx)
{
    [Fact]
    public async Task Outbox_changes_can_be_pulled_then_acked_and_disappear_from_the_feed()
    {
        var (client, _, _, _) = fx.NewClient(); // fresh tenant => the only outbox rows are this test's

        // A completed sale writes domain events (incl. SaleCompleted) to the outbox in the same tx.
        var product = await CreateProduct(client, $"SYN-{Guid.NewGuid():N}"[..14], 100m);
        var register = await client.OpenShiftAsync();
        var checkout = new CheckoutRequest(
            RegisterId: register,
            Lines: [new CheckoutLineRequest(product.Id, 1)],
            Tenders: [new CheckoutTenderRequest(TenderType.Cash, 100m)],
            Currency: "KES",
            SaleId: Guid.CreateVersion7());
        (await client.PostAsJsonAsync("/api/v1/sales/checkout", checkout, PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Pull the feed — at least the SaleCompleted change is present.
        var feed = (await (await client.GetAsync("/api/v1/sync/changes")).Content
            .ReadFromJsonAsync<ChangeFeedResponse>(PosApiFixture.Json))!;
        feed.Changes.Should().NotBeEmpty();
        feed.Changes.Should().Contain(c => c.EventType.Contains("SaleCompleted"));
        feed.Changes.Should().OnlyContain(c => c.Payload.Length > 0);
        feed.HasMore.Should().BeFalse("the batch is larger than the handful of events this test produced");

        // Ack the whole batch.
        var ids = feed.Changes.Select(c => c.Id).ToList();
        var ack = (await (await client.PostAsJsonAsync("/api/v1/sync/ack", new AckRequest(ids), PosApiFixture.Json))
            .Content.ReadFromJsonAsync<AckResponse>(PosApiFixture.Json))!;
        ack.Acknowledged.Should().Be(ids.Count);

        // The acked changes are gone from the feed (idempotent shipping marker).
        var after = (await (await client.GetAsync("/api/v1/sync/changes")).Content
            .ReadFromJsonAsync<ChangeFeedResponse>(PosApiFixture.Json))!;
        after.Changes.Should().BeEmpty();
        after.Remaining.Should().Be(0);

        // Re-acking already-shipped ids is a harmless no-op (at-least-once friendly).
        var reack = (await (await client.PostAsJsonAsync("/api/v1/sync/ack", new AckRequest(ids), PosApiFixture.Json))
            .Content.ReadFromJsonAsync<AckResponse>(PosApiFixture.Json))!;
        reack.Acknowledged.Should().Be(0);
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
