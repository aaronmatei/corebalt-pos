using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Pos.Api.Auth;
using Pos.Api.Contracts;
using Pos.Domain.Tenancy;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Entitlements are VENDOR-controlled: they come only from a Corebalt-signed licence key (verified with
/// the embedded public key). A client can apply a key but cannot edit edition/flags/limits; a tampered,
/// expired, or wrong-tenant key is rejected, so a client can't self-upgrade.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class LicenseTests(PosApiFixture fx)
{
    private static readonly CreateBranchRequest Branch = new("West", "WB", "Westlands");

    [Fact]
    public async Task A_valid_signed_licence_applies_its_entitlements_and_unlocks_the_gated_module()
    {
        var (tenant, store) = NewProvisionedUnlicensed();
        var client = ClientFor(tenant, store);

        // Unlicensed baseline: MultiBranch denied.
        (await client.PostAsJsonAsync("/api/v1/branches", Branch, PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Apply a real Corebalt-signed licence for THIS tenant.
        var apply = await client.PostAsJsonAsync("/api/v1/license",
            new ApplyLicenseRequest(LicenseTestSigner.Standard(tenant)), PosApiFixture.Json);
        apply.StatusCode.Should().Be(HttpStatusCode.OK);

        var ent = (await (await client.GetAsync("/api/v1/entitlements"))
            .Content.ReadFromJsonAsync<EntitlementsResponse>(PosApiFixture.Json))!;
        ent.Edition.Should().Be("Supermarket");
        ent.Features.Should().Contain("MultiBranch");

        // The module is now unlocked.
        (await client.PostAsJsonAsync("/api/v1/branches", Branch, PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_tampered_licence_is_rejected_and_grants_nothing()
    {
        var (tenant, store) = NewProvisionedUnlicensed();
        var client = ClientFor(tenant, store);

        var token = LicenseTestSigner.Standard(tenant);
        var tampered = token[..20] + (token[20] == 'A' ? 'B' : 'A') + token[21..]; // flip one char

        (await client.PostAsJsonAsync("/api/v1/license", new ApplyLicenseRequest(tampered), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // No self-upgrade: still on the baseline, module still blocked.
        (await client.PostAsJsonAsync("/api/v1/branches", Branch, PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_expired_licence_is_rejected()
    {
        var (tenant, store) = NewProvisionedUnlicensed();
        var client = ClientFor(tenant, store);

        var expired = LicenseTestSigner.Sign(tenant, Edition.Supermarket, Feature.MultiBranch, 8, 10,
            validUntil: DateTimeOffset.UtcNow.AddDays(-1));

        (await client.PostAsJsonAsync("/api/v1/license", new ApplyLicenseRequest(expired), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_licence_issued_for_another_tenant_is_rejected()
    {
        var (tenant, store) = NewProvisionedUnlicensed();
        var client = ClientFor(tenant, store);

        var otherTenantsKey = LicenseTestSigner.Standard(Uuid7.NewGuid()); // valid signature, wrong tenant

        (await client.PostAsJsonAsync("/api/v1/license", new ApplyLicenseRequest(otherTenantsKey), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private (Guid tenant, Guid store) NewProvisionedUnlicensed()
    {
        var tenant = Uuid7.NewGuid();
        var store = Uuid7.NewGuid();
        fx.Provision(tenant, store, r => r with { LicenseKey = null }); // merchant set up, but no licence yet
        return (tenant, store);
    }

    private HttpClient ClientFor(Guid tenant, Guid store)
    {
        var client = fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthMiddleware.TenantHeader, tenant.ToString());
        client.DefaultRequestHeaders.Add(DevHeaderAuthMiddleware.StoreHeader, store.ToString());
        client.DefaultRequestHeaders.Add(DevHeaderAuthMiddleware.UserHeader, Uuid7.NewGuid().ToString());
        return client;
    }
}
