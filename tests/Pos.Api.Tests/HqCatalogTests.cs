using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Pos.Api.Contracts;
using Pos.Application.Sync;
using Pos.Application.Tenancy;
using Pos.Domain.Catalog;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// M2 — HQ central catalogue: a manager edits the catalogue in the cloud; every change appends to the
/// feed; a store pulls the feed (cursor-based, sync-token auth) to apply down. Runs in Hq mode.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class HqCatalogTests(PosApiFixture fx)
{
    private const string Base = "pos.localhost";
    private const string AdminToken = "test-admin-token";

    private WebApplicationFactory<Program> HqFactory() => fx.Factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Deployment:Mode", "Hq");
        b.UseSetting("Deployment:TenantBaseDomain", Base);
        b.UseSetting("Admin:ApiToken", AdminToken);
        b.UseSetting("AllowedHosts", "*");
        b.UseSetting("Auth:AllowDevHeaders", "false");
    });

    private static string Slug(string p) => $"{p}-{Guid.NewGuid():N}"[..14];

    private static async Task<ProvisionedTenant> Provision(WebApplicationFactory<Program> f, string slug)
    {
        var admin = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{Base}/") });
        admin.DefaultRequestHeaders.Add("X-Admin-Token", AdminToken);
        var resp = await admin.PostAsJsonAsync("/admin/tenants", new
        {
            slug, displayName = $"{slug} Ltd", kraPin = "P051234567X",
            managerUsername = "manager", managerPassword = "Pass!234",
        }, PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<ProvisionedTenant>(PosApiFixture.Json))!;
    }

    private static async Task<HttpClient> ManagerClient(WebApplicationFactory<Program> f, ProvisionedTenant t)
    {
        var login = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{t.Slug}.{Base}/") });
        var resp = await login.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("manager", "Pass!234"), PosApiFixture.Json);
        var token = (await resp.Content.ReadFromJsonAsync<TokenResponse>(PosApiFixture.Json))!.AccessToken;
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{t.Slug}.{Base}/") });
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static HttpClient PullClient(WebApplicationFactory<Program> f, string? token)
    {
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{Base}/") });
        if (token is not null) c.DefaultRequestHeaders.Add("X-Sync-Token", token);
        return c;
    }

    [Fact]
    public async Task Manage_catalogue_appends_to_the_feed_which_a_store_pulls_by_cursor()
    {
        var f = HqFactory();
        var tenant = await Provision(f, Slug("acme"));
        var hq = await ManagerClient(f, tenant);

        // HQ creates two items and reprices one → 3 feed entries.
        var milk = (await (await hq.PostAsJsonAsync("/api/v1/catalog", new CreateCatalogItemRequest(
            "MILK", "Milk 500ml", 60m, "KES", UnitOfMeasure.Each, null, TaxClass.StandardRated), PosApiFixture.Json))
            .Content.ReadFromJsonAsync<CatalogItemResponse>(PosApiFixture.Json))!;
        (await hq.PostAsJsonAsync("/api/v1/catalog", new CreateCatalogItemRequest(
            "SUGAR", "Sugar 1kg", 150m, "KES", UnitOfMeasure.Each, null, TaxClass.StandardRated), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await hq.PutAsJsonAsync($"/api/v1/catalog/{milk.Id}/price", new RepriceProductRequest(65m, "KES"), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // A store pulls the feed from cursor 0.
        var pull = PullClient(f, tenant.SyncToken);
        var page = (await (await pull.GetAsync($"/hq/catalog/changes?slug={tenant.Slug}&since=0"))
            .Content.ReadFromJsonAsync<CatalogPullResponse>(PosApiFixture.Json))!;
        page.Items.Should().HaveCount(3);
        page.Items.Should().ContainSingle(i => i.Sku == "MILK" && i.PriceAmount == 65m); // latest snapshot
        page.Items.Should().ContainSingle(i => i.Sku == "SUGAR" && i.PriceAmount == 150m);
        page.Cursor.Should().BeGreaterThan(0);

        // Re-pull from the cursor → nothing new (idempotent catch-up).
        var empty = (await (await pull.GetAsync($"/hq/catalog/changes?slug={tenant.Slug}&since={page.Cursor}"))
            .Content.ReadFromJsonAsync<CatalogPullResponse>(PosApiFixture.Json))!;
        empty.Items.Should().BeEmpty();
        empty.Cursor.Should().Be(page.Cursor);
    }

    [Fact]
    public async Task Pull_rejects_a_bad_token()
    {
        var f = HqFactory();
        var tenant = await Provision(f, Slug("acme"));
        (await PullClient(f, "hqs_wrong").GetAsync($"/hq/catalog/changes?slug={tenant.Slug}&since=0"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PullClient(f, null).GetAsync($"/hq/catalog/changes?slug={tenant.Slug}&since=0"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
