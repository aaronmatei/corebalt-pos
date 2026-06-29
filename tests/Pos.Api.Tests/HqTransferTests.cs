using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Pos.Application.Sync;
using Pos.Application.Tenancy;
using Pos.Domain.Inventory.Events;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// M3 cloud routing: a dispatched inter-branch transfer is ingested into hq_transfers, the source branch
/// self-registers, the destination pulls it from /hq/transfers/incoming, acks receipt, and it disappears.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class HqTransferTests(PosApiFixture fx)
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

    private static HttpClient Token(WebApplicationFactory<Program> f, string? token)
    {
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{Base}/") });
        if (token is not null) c.DefaultRequestHeaders.Add("X-Sync-Token", token);
        return c;
    }

    [Fact]
    public async Task Dispatched_transfer_is_routed_received_and_the_branch_is_registered()
    {
        var f = HqFactory();
        var tenant = await Provision(f, Slug("acme"));
        var fromStore = Uuid7.NewGuid();
        var toStore = Uuid7.NewGuid();
        var transferId = Uuid7.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var snap = new TransferSnapshot(transferId, tenant.TenantId, fromStore, toStore, "West Branch",
            "Asha Manager", now, "restock", new[] { new TransferLineSnapshot(Uuid7.NewGuid(), "MILK", "Milk 500ml", 12m) });
        var change = new SyncChangeDto(Uuid7.NewGuid(), transferId, typeof(StockTransferDispatched).FullName!,
            now, now, "{}", JsonSerializer.Serialize(snap, PosApiFixture.Json));
        // The source store pushes — StoreName self-registers it in the branch registry.
        var batch = new SyncIngestRequest(tenant.Slug, fromStore, new[] { change }, "Main Branch");

        var sync = Token(f, tenant.SyncToken);
        (await sync.PostAsJsonAsync("/hq/sync/ingest", batch, PosApiFixture.Json)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Destination pulls its incoming transfers.
        var inc = (await (await sync.GetAsync($"/hq/transfers/incoming?slug={tenant.Slug}&storeId={toStore}"))
            .Content.ReadFromJsonAsync<IncomingTransfersResponse>(PosApiFixture.Json))!;
        var t = inc.Transfers.Should().ContainSingle(x => x.TransferId == transferId).Subject;
        t.ToStoreName.Should().Be("West Branch");
        t.Lines.Should().ContainSingle(l => l.Sku == "MILK" && l.Quantity == 12m);

        // The source branch is now discoverable as a destination from the other branch.
        var branches = (await (await sync.GetAsync($"/hq/branches?slug={tenant.Slug}&storeId={toStore}"))
            .Content.ReadFromJsonAsync<BranchesResponse>(PosApiFixture.Json))!;
        branches.Branches.Should().Contain(b => b.StoreId == fromStore && b.Name == "Main Branch");

        // Destination acks receipt → it's gone from incoming.
        (await sync.PostAsync($"/hq/transfers/{transferId}/received?slug={tenant.Slug}&storeId={toStore}", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var inc2 = (await (await sync.GetAsync($"/hq/transfers/incoming?slug={tenant.Slug}&storeId={toStore}"))
            .Content.ReadFromJsonAsync<IncomingTransfersResponse>(PosApiFixture.Json))!;
        inc2.Transfers.Should().NotContain(x => x.TransferId == transferId);
    }

    [Fact]
    public async Task Incoming_pull_rejects_a_bad_token()
    {
        var f = HqFactory();
        var tenant = await Provision(f, Slug("acme"));
        (await Token(f, "hqs_wrong").GetAsync($"/hq/transfers/incoming?slug={tenant.Slug}&storeId={Uuid7.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
