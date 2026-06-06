using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Api.Contracts;
using Pos.Domain.Catalog;
using Pos.Infrastructure.Persistence;
using Xunit;

namespace Pos.Api.Tests;

[Collection(PosApiCollection.Name)]
public sealed class ProductManagementTests(PosApiFixture fx)
{
    [Fact]
    public async Task Duplicate_sku_is_rejected()
    {
        var (client, _, _, _) = fx.NewClient();
        var sku = $"DUP-{Guid.NewGuid():N}"[..12];

        (await CreateRaw(client, sku, "First")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CreateRaw(client, sku, "Second")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Duplicate_barcode_is_rejected()
    {
        var (client, _, _, _) = fx.NewClient();
        var barcode = $"61610{Guid.NewGuid().GetHashCode() & 0xFFFFF}";

        (await CreateRaw(client, $"S1-{Guid.NewGuid():N}"[..12], "First", barcode)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CreateRaw(client, $"S2-{Guid.NewGuid():N}"[..12], "Second", barcode)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Price_change_emits_ProductPriceChanged_to_the_outbox()
    {
        var (client, _, _, user) = fx.NewClient();
        var product = (await (await CreateRaw(client, $"P-{Guid.NewGuid():N}"[..12], "Milk", price: 100m))
            .Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;

        var resp = await client.PutAsJsonAsync($"/api/v1/products/{product.Id}/price",
            new RepriceProductRequest(150m, "KES"), PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
        var events = await db.OutboxMessages
            .Where(m => m.AggregateId == product.Id && m.EventType.Contains("ProductPriceChanged"))
            .ToListAsync();

        events.Should().ContainSingle();
        var payload = events[0].Payload;
        payload.Should().Contain("100").And.Contain("150");        // old → new
        payload.Should().Contain(user.ToString());                 // who
        events[0].OccurredAtUtc.Should().NotBe(default);           // when
    }

    [Fact]
    public async Task Setting_the_same_price_emits_nothing()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = (await (await CreateRaw(client, $"P-{Guid.NewGuid():N}"[..12], "Soda", price: 80m))
            .Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;

        await client.PutAsJsonAsync($"/api/v1/products/{product.Id}/price", new RepriceProductRequest(80m, "KES"), PosApiFixture.Json);

        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
        (await db.OutboxMessages.CountAsync(m => m.AggregateId == product.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Deactivate_hides_from_default_list_but_shows_with_includeInactive()
    {
        var (client, _, _, _) = fx.NewClient(); // fresh store → list is exactly what we add
        var keep = (await (await CreateRaw(client, $"K-{Guid.NewGuid():N}"[..12], "Keep"))
            .Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
        var drop = (await (await CreateRaw(client, $"D-{Guid.NewGuid():N}"[..12], "Drop"))
            .Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;

        var deact = await client.PostAsync($"/api/v1/products/{drop.Id}/deactivate", null);
        deact.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deact.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!.IsActive.Should().BeFalse();

        var active = (await (await client.GetAsync("/api/v1/products"))
            .Content.ReadFromJsonAsync<List<ProductResponse>>(PosApiFixture.Json))!;
        active.Select(p => p.Id).Should().Contain(keep.Id).And.NotContain(drop.Id);

        var all = (await (await client.GetAsync("/api/v1/products?includeInactive=true"))
            .Content.ReadFromJsonAsync<List<ProductResponse>>(PosApiFixture.Json))!;
        all.Select(p => p.Id).Should().Contain(new[] { keep.Id, drop.Id });
    }

    private static Task<HttpResponseMessage> CreateRaw(HttpClient client, string sku, string name, string? barcode = null, decimal price = 100m) =>
        client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            Sku: sku, Name: name, PriceAmount: price, PriceCurrency: "KES",
            UnitOfMeasure: UnitOfMeasure.Each, Barcode: barcode, TaxClass: TaxClass.StandardRated), PosApiFixture.Json);
}
