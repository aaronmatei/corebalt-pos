using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Pos.Api.Contracts;
using Pos.Domain.Catalog;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// HQ / cloud (multi-tenant SaaS) mode: the active tenant is resolved from the request subdomain
/// (acme.pos.* → slug "acme"), login is scoped to that tenant, tenant data is isolated, a token from one
/// tenant can't act on another's subdomain, and tenants are admin-provisioned (no /setup wizard).
/// Runs the SAME binary as the on-prem tests, just with Deployment:Mode=Hq.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class HqMultiTenantTests(PosApiFixture fx)
{
    private const string Base = "pos.localhost";
    private const string AdminToken = "test-admin-token";
    private const string AdminTokenHeader = "X-Admin-Token";

    private WebApplicationFactory<Program> HqFactory() => fx.Factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Deployment:Mode", "Hq");
        b.UseSetting("Deployment:TenantBaseDomain", Base);
        b.UseSetting("Admin:ApiToken", AdminToken);
        b.UseSetting("AllowedHosts", "*");
        b.UseSetting("Auth:AllowDevHeaders", "false"); // Hq uses real JWT auth, never the dev bypass
        b.UseSetting("StoreServer:TenantId", "");      // no single configured tenant in the cloud
        b.UseSetting("StoreServer:StoreId", "");
    });

    // Unique per run so reruns against the persistent pos_test DB never collide on the slug.
    private static string Slug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..14];

    private static HttpClient ClientFor(WebApplicationFactory<Program> f, string host) =>
        f.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri($"http://{host}/"),
        });

    private static object TenantBody(string slug, string user, string pass) => new
    {
        slug,
        displayName = $"{slug} Ltd",
        kraPin = "P051234567X",
        managerName = "Manager",
        managerUsername = user,
        managerPassword = pass,
    };

    private static async Task ProvisionTenant(WebApplicationFactory<Program> f, string slug, string user, string pass)
    {
        var admin = ClientFor(f, Base); // apex host (no subdomain) is the admin surface
        admin.DefaultRequestHeaders.Add(AdminTokenHeader, AdminToken);
        var resp = await admin.PostAsJsonAsync("/admin/tenants", TenantBody(slug, user, pass), PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
    }

    private static async Task<string> LoginTokenAsync(WebApplicationFactory<Program> f, string slug, string user, string pass)
    {
        var client = ClientFor(f, $"{slug}.{Base}");
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(user, pass), PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<TokenResponse>(PosApiFixture.Json))!.AccessToken;
    }

    private static HttpClient Bearer(WebApplicationFactory<Program> f, string slug, string token)
    {
        var client = ClientFor(f, $"{slug}.{Base}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Login_is_scoped_to_the_subdomain_and_unknown_slugs_404()
    {
        var f = HqFactory();
        var acme = Slug("acme"); var globex = Slug("globex");
        await ProvisionTenant(f, acme, "amgr", "Pass!234");
        await ProvisionTenant(f, globex, "gmgr", "Pass!234");

        // The manager authenticates on their OWN subdomain.
        (await ClientFor(f, $"{acme}.{Base}").PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("amgr", "Pass!234"), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // The SAME credentials are rejected on a DIFFERENT tenant's subdomain (not a user there).
        (await ClientFor(f, $"{globex}.{Base}").PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("amgr", "Pass!234"), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // An unknown subdomain is 404 before anything tenant-scoped runs.
        (await ClientFor(f, $"{Slug("nobody")}.{Base}").GetAsync("/"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tenant_data_is_isolated_across_subdomains()
    {
        var f = HqFactory();
        var acme = Slug("acme"); var globex = Slug("globex");
        await ProvisionTenant(f, acme, "amgr", "Pass!234");
        await ProvisionTenant(f, globex, "gmgr", "Pass!234");

        var acmeClient = Bearer(f, acme, await LoginTokenAsync(f, acme, "amgr", "Pass!234"));
        var sku = $"HQ-{Guid.NewGuid():N}"[..10];
        var create = await acmeClient.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            sku, "Sugar 1kg", 150m, "KES", UnitOfMeasure.Each, null, TaxClass.StandardRated), PosApiFixture.Json);
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var product = (await create.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;

        // The owning tenant sees it…
        (await acmeClient.GetAsync($"/api/v1/products/{product.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // …another tenant, on its own subdomain with its own token, does NOT.
        var globexClient = Bearer(f, globex, await LoginTokenAsync(f, globex, "gmgr", "Pass!234"));
        (await globexClient.GetAsync($"/api/v1/products/{product.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_tokens_tenant_must_match_the_subdomain_it_is_used_on()
    {
        var f = HqFactory();
        var acme = Slug("acme"); var globex = Slug("globex");
        await ProvisionTenant(f, acme, "amgr", "Pass!234");
        await ProvisionTenant(f, globex, "gmgr", "Pass!234");

        var acmeToken = await LoginTokenAsync(f, acme, "amgr", "Pass!234");

        // acme's token replayed against globex's subdomain → blocked by the tenant guard.
        var crossed = Bearer(f, globex, acmeToken);
        (await crossed.GetAsync("/api/v1/products")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_provisioning_requires_the_token_and_validates_the_slug()
    {
        var f = HqFactory();
        var apex = ClientFor(f, Base);

        // No admin token → refused.
        (await apex.PostAsJsonAsync("/admin/tenants", TenantBody(Slug("acme"), "mgr", "Pass!234"), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        apex.DefaultRequestHeaders.Add(AdminTokenHeader, AdminToken);

        // Reserved slug → rejected.
        (await apex.PostAsJsonAsync("/admin/tenants", TenantBody("admin", "mgr", "Pass!234"), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Valid → created; the same slug again → conflict.
        var slug = Slug("acme");
        (await apex.PostAsJsonAsync("/admin/tenants", TenantBody(slug, "mgr", "Pass!234"), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await apex.PostAsJsonAsync("/admin/tenants", TenantBody(slug, "mgr2", "Pass!234"), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
