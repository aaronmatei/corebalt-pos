using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Pos.Api.Auth;
using Pos.Api.Contracts;
using Pos.Application.Identity;
using Pos.Application.Tenancy;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Pos.Domain.Tenancy;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Per-client installs: DB-backed merchant identity, per-tenant integration settings (encrypted),
/// entitlements gating, and the first-run setup gate. Corebalt is the vendor; nothing client-specific
/// is hardcoded.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class TenancySetupTests(PosApiFixture fx)
{
    [Fact]
    public async Task Fresh_install_with_no_profile_routes_to_the_setup_wizard()
    {
        // A host whose StoreServer tenant has NOT been provisioned.
        using var fresh = fx.Factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<StoreServerOptions>();
            s.AddSingleton(new StoreServerOptions { TenantId = Uuid7.NewGuid(), StoreId = Uuid7.NewGuid() });
        }));
        var client = fresh.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/");
        resp.StatusCode.Should().Be(HttpStatusCode.Found);
        resp.Headers.Location!.OriginalString.Should().Contain("/setup");
    }

    [Fact]
    public async Task A_fresh_tenant_cannot_transact_until_configured()
    {
        var (client, _, _, _) = fx.NewClient(provision: false); // un-configured tenant

        var resp = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: Uuid7.NewGuid(),
            Lines: new[] { new CheckoutLineRequest(Uuid7.NewGuid(), 1m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 100m, null) }), PosApiFixture.Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict); // "not set up yet"
    }

    [Fact]
    public async Task The_manager_created_by_setup_logs_in_and_there_is_no_seeded_default()
    {
        // The StoreServer tenant is provisioned by the wizard path (SetupService) with this manager —
        // no hardcoded seeded credential exists.
        var client = fx.Factory.CreateClient();

        var good = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(PosApiFixture.ManagerUsername, PosApiFixture.ManagerPassword), PosApiFixture.Json);
        good.StatusCode.Should().Be(HttpStatusCode.OK);

        // A guessed default does NOT exist.
        var bad = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("admin", "admin"), PosApiFixture.Json);
        bad.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task After_provisioning_the_receipt_shows_the_clients_identity_with_powered_by()
    {
        var (client, _, _, _) = fx.NewClient(); // provisions "Test Retailer Ltd"
        var product = await CreateProduct(client, 100m);
        var saleId = await Checkout(client, product.Id);

        var receipt = (await (await client.GetAsync($"/api/v1/sales/{saleId}/receipt"))
            .Content.ReadFromJsonAsync<ReceiptResponse>(PosApiFixture.Json))!;

        receipt.Model.Header.LegalName.Should().Be("Test Retailer Ltd");   // the client, not Corebalt
        receipt.Model.Header.KraPin.Should().Be("P051234567X");
        receipt.Model.Header.LegalName.Should().NotContain("Corebalt");
        receipt.Text.Should().Contain("Test Retailer Ltd");
        receipt.Text.Should().Contain("Powered by Corebalt POS");          // the optional vendor footer
    }

    [Fact]
    public async Task A_disabled_feature_flag_blocks_that_module()
    {
        // Tenant on the unlicensed baseline (no licence key) → no MultiBranch → adding a branch is blocked.
        var noMulti = Uuid7.NewGuid(); var noMultiStore = Uuid7.NewGuid();
        fx.Provision(noMulti, noMultiStore, r => r with { LicenseKey = null });
        var blocked = ClientFor(noMulti, noMultiStore);
        (await blocked.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest("West", "WB", "Westlands"), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Tenant WITH MultiBranch (default provisioning) → allowed.
        var (allowed, _, _, _) = fx.NewClient();
        (await allowed.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest("West", "WB", "Westlands"), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Integration_settings_are_per_tenant_and_encrypted_at_rest()
    {
        var tenant = Uuid7.NewGuid(); var store = Uuid7.NewGuid();
        fx.Provision(tenant, store, r => r with
        {
            MpesaEnabled = true, MpesaShortCode = "654321", MpesaConsumerKey = "ck", MpesaConsumerSecret = "supersecret", MpesaPasskey = "pk",
            EtimsEnabled = true, EtimsMode = EtimsMode.Oscu,
        });

        // The providers read these PER TENANT (decrypted) from the DB — not appsettings.
        using (var scope = fx.Factory.Services.CreateScope())
        {
            var mpesa = await scope.ServiceProvider.GetRequiredService<IMpesaSettingsRepository>().GetAsync(tenant);
            mpesa!.ShortCode.Should().Be("654321");
            mpesa.ConsumerSecret.Should().Be("supersecret"); // round-trips decrypted
            mpesa.Passkey.Should().Be("pk");
            var etims = await scope.ServiceProvider.GetRequiredService<IEtimsSettingsRepository>().GetAsync(tenant);
            etims!.Enabled.Should().BeTrue();
            etims.Mode.Should().Be(EtimsMode.Oscu);
        }

        // At rest the secret is ciphertext, never the plaintext.
        await using var conn = new NpgsqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT consumer_secret FROM mpesa_settings WHERE tenant_id = @t";
        cmd.Parameters.AddWithValue("@t", tenant);
        var stored = (string)(await cmd.ExecuteScalarAsync())!;
        stored.Should().StartWith("dp:").And.NotContain("supersecret"); // Data Protection ciphertext, not plaintext
    }

    // ── helpers ──
    private HttpClient ClientFor(Guid tenant, Guid store)
    {
        var client = fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthMiddleware.TenantHeader, tenant.ToString());
        client.DefaultRequestHeaders.Add(DevHeaderAuthMiddleware.StoreHeader, store.ToString());
        client.DefaultRequestHeaders.Add(DevHeaderAuthMiddleware.UserHeader, Uuid7.NewGuid().ToString());
        return client;
    }

    private static async Task<ProductResponse> CreateProduct(HttpClient client, decimal price)
    {
        var sku = $"TN-{Guid.NewGuid():N}"[..12];
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            sku, $"Item {sku}", price, "KES", UnitOfMeasure.Each, null, TaxClass.StandardRated), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }

    private static async Task<Guid> Checkout(HttpClient client, Guid productId)
    {
        var register = await client.OpenShiftAsync();
        var resp = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: register,
            Lines: new[] { new CheckoutLineRequest(productId, 1m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 200m, null) }), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CompleteSaleResponse>(PosApiFixture.Json))!.SaleId;
    }
}
