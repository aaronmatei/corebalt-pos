using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Sync;
using Pos.Application.Tenancy;
using Pos.Domain.Cash.Events;
using Pos.Domain.Sales.Events;
using Pos.Infrastructure.Persistence;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// HQ/cloud store→cloud sync ingest: a store pushes a batch of outbox changes; the cloud durably stores
/// them (idempotent) and projects SaleCompleted into the hq_sales read-model, isolated per tenant and
/// authenticated by the tenant's sync token.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class HqSyncIngestTests(PosApiFixture fx)
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
            managerUsername = "mgr", managerPassword = "Pass!234",
        }, PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<ProvisionedTenant>(PosApiFixture.Json))!;
    }

    private static HttpClient IngestClient(WebApplicationFactory<Program> f, string? token)
    {
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{Base}/") });
        if (token is not null) c.DefaultRequestHeaders.Add("X-Sync-Token", token);
        return c;
    }

    private static SyncIngestRequest SaleBatch(ProvisionedTenant t, Guid changeId, Guid saleId, decimal total)
    {
        var snap = new SaleSnapshot(saleId, t.TenantId, t.StoreId, "MB-000123", Uuid7.NewGuid(), "Lane 1",
            Uuid7.NewGuid(), "Jane Cashier", null, "KES", total, 16m, DateTimeOffset.UtcNow,
            new[] { new SaleLineSnapshot(Uuid7.NewGuid(), "Sugar 1kg", 1m, total, total, "StandardRated", 16m) },
            new[] { new SaleTenderSnapshot("Cash", total, "Confirmed", null) });
        var change = new SyncChangeDto(changeId, saleId, typeof(SaleCompleted).FullName!,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "{}", JsonSerializer.Serialize(snap, PosApiFixture.Json));
        return new SyncIngestRequest(t.Slug, t.StoreId, new[] { change });
    }

    [Fact]
    public async Task Ingest_projects_a_sale_and_is_idempotent()
    {
        var f = HqFactory();
        var tenant = await Provision(f, Slug("acme"));
        var client = IngestClient(f, tenant.SyncToken);

        var changeId = Uuid7.NewGuid();
        var saleId = Uuid7.NewGuid();
        var batch = SaleBatch(tenant, changeId, saleId, 250m);

        var first = await client.PostAsJsonAsync("/hq/sync/ingest", batch, PosApiFixture.Json);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var accepted = (await first.Content.ReadFromJsonAsync<SyncIngestResponse>(PosApiFixture.Json))!;
        accepted.AcceptedIds.Should().Contain(changeId);

        // The sale projected into hq_sales (queried in a bare scope → unfiltered, sees the row).
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
            var row = await db.HqSales.FirstOrDefaultAsync(s => s.Id == saleId);
            row.Should().NotBeNull();
            row!.TenantId.Should().Be(tenant.TenantId);
            row.GrandTotal.Should().Be(250m);
            row.LineCount.Should().Be(1);
            (await db.SyncInbox.CountAsync(e => e.Id == changeId)).Should().Be(1);
        }

        // Re-send the same batch → accepted again, but NO duplicate row/inbox entry.
        var second = await client.PostAsJsonAsync("/hq/sync/ingest", batch, PosApiFixture.Json);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
            (await db.HqSales.CountAsync(s => s.Id == saleId)).Should().Be(1);
            (await db.SyncInbox.CountAsync(e => e.Id == changeId)).Should().Be(1);
        }
    }

    [Fact]
    public async Task Ingest_projects_a_closed_session()
    {
        var f = HqFactory();
        var tenant = await Provision(f, Slug("acme"));

        var sessionId = Uuid7.NewGuid();
        var snap = new SessionSnapshot(sessionId, tenant.TenantId, tenant.StoreId, Uuid7.NewGuid(), "Lane 2",
            "Asha Manager", DateTimeOffset.UtcNow.AddHours(-8), 1000m, "Asha Manager", DateTimeOffset.UtcNow,
            5000m, 5100m, -100m, true, "KES");
        var change = new SyncChangeDto(Uuid7.NewGuid(), sessionId, typeof(RegisterSessionClosed).FullName!,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "{}", JsonSerializer.Serialize(snap, PosApiFixture.Json));
        var batch = new SyncIngestRequest(tenant.Slug, tenant.StoreId, new[] { change });

        var resp = await IngestClient(f, tenant.SyncToken).PostAsJsonAsync("/hq/sync/ingest", batch, PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());

        using var scope = f.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IHqSessionsReadStore>();
        var rows = await sessions.RecentAsync(tenant.TenantId, 50);
        var row = rows.Should().ContainSingle(r => r.Id == sessionId).Subject;
        row.RegisterLabel.Should().Be("Lane 2");
        row.Variance.Should().Be(-100m);
        row.ExpectedCash.Should().Be(5100m);
    }

    [Fact]
    public async Task Ingest_sums_stock_movements_into_on_hand_once()
    {
        var f = HqFactory();
        var tenant = await Provision(f, Slug("acme"));
        var productId = Uuid7.NewGuid();

        SyncChangeDto StockChange(decimal delta) => new(Uuid7.NewGuid(), Uuid7.NewGuid(),
            typeof(Pos.Domain.Inventory.Events.StockMovementRecorded).FullName!,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "{}",
            JsonSerializer.Serialize(new StockMovementSnapshot(Uuid7.NewGuid(), tenant.TenantId, tenant.StoreId,
                productId, "SUG-1", "Sugar 1kg", "Each", delta, "Purchase", DateTimeOffset.UtcNow), PosApiFixture.Json));

        var batch = new SyncIngestRequest(tenant.Slug, tenant.StoreId, new[] { StockChange(+24m), StockChange(-2m) });
        var client = IngestClient(f, tenant.SyncToken);
        (await client.PostAsJsonAsync("/hq/sync/ingest", batch, PosApiFixture.Json)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Re-send the SAME batch: idempotent — deltas are not double-counted.
        (await client.PostAsJsonAsync("/hq/sync/ingest", batch, PosApiFixture.Json)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = f.Services.CreateScope();
        var stock = scope.ServiceProvider.GetRequiredService<IHqStockReadStore>();
        var row = (await stock.AllAsync(tenant.TenantId, 100)).Should().ContainSingle(r => r.ProductId == productId).Subject;
        row.OnHand.Should().Be(22m); // 24 − 2, counted once despite two POSTs
        row.Name.Should().Be("Sugar 1kg");
    }

    [Fact]
    public async Task Ingest_projects_a_credit_note()
    {
        var f = HqFactory();
        var tenant = await Provision(f, Slug("acme"));
        var noteId = Uuid7.NewGuid();

        var snap = new CreditNoteSnapshot(noteId, tenant.TenantId, tenant.StoreId, "MB-CN-000124",
            Uuid7.NewGuid(), "MB-000123", "Damaged", false, "Asha Manager", "Cash", "Refunded", 150m, "KES", 1, DateTimeOffset.UtcNow);
        var change = new SyncChangeDto(Uuid7.NewGuid(), noteId, typeof(CreditNoteIssued).FullName!,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "{}", JsonSerializer.Serialize(snap, PosApiFixture.Json));
        var batch = new SyncIngestRequest(tenant.Slug, tenant.StoreId, new[] { change });

        (await IngestClient(f, tenant.SyncToken).PostAsJsonAsync("/hq/sync/ingest", batch, PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = f.Services.CreateScope();
        var returns = scope.ServiceProvider.GetRequiredService<IHqCreditNotesReadStore>();
        var row = (await returns.RecentAsync(tenant.TenantId, 50)).Should().ContainSingle(r => r.Id == noteId).Subject;
        row.ReturnNumber.Should().Be("MB-CN-000124");
        row.GrandTotal.Should().Be(150m);
        row.Reason.Should().Be("Damaged");
    }

    [Fact]
    public async Task Ingest_is_isolated_per_tenant()
    {
        var f = HqFactory();
        var acme = await Provision(f, Slug("acme"));
        var globex = await Provision(f, Slug("globex"));

        var saleId = Uuid7.NewGuid();
        await IngestClient(f, acme.SyncToken).PostAsJsonAsync("/hq/sync/ingest",
            SaleBatch(acme, Uuid7.NewGuid(), saleId, 99m), PosApiFixture.Json);

        using var scope = f.Services.CreateScope();
        var reads = scope.ServiceProvider.GetRequiredService<IHqSalesReadStore>();
        (await reads.RecentAsync(acme.TenantId, 50)).Sales.Should().Contain(s => s.Id == saleId);
        (await reads.RecentAsync(globex.TenantId, 50)).Sales.Should().NotContain(s => s.Id == saleId);
    }

    [Fact]
    public async Task Sync_status_reflects_received_changes_per_branch()
    {
        var f = HqFactory();
        var tenant = await Provision(f, Slug("acme"));
        await IngestClient(f, tenant.SyncToken).PostAsJsonAsync("/hq/sync/ingest",
            SaleBatch(tenant, Uuid7.NewGuid(), Uuid7.NewGuid(), 75m), PosApiFixture.Json);

        using var scope = f.Services.CreateScope();
        var status = await scope.ServiceProvider.GetRequiredService<IHqSyncStatusReadStore>().GetAsync(tenant.TenantId);
        status.TotalChanges.Should().BeGreaterThanOrEqualTo(1);
        status.LastReceivedAtUtc.Should().NotBeNull();
        status.Stores.Should().Contain(s => s.StoreId == tenant.StoreId && s.Changes >= 1);
    }

    [Fact]
    public async Task Rotating_the_sync_token_invalidates_the_old_one()
    {
        var f = HqFactory();
        var tenant = await Provision(f, Slug("acme"));

        var admin = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{Base}/") });
        admin.DefaultRequestHeaders.Add("X-Admin-Token", AdminToken);
        var rotateResp = await admin.PostAsync($"/admin/tenants/{tenant.Slug}/rotate-sync-token", null);
        rotateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = System.Text.Json.JsonDocument.Parse(await rotateResp.Content.ReadAsStringAsync());
        var newToken = doc.RootElement.GetProperty("syncToken").GetString()!;
        newToken.Should().NotBe(tenant.SyncToken);

        var batch = SaleBatch(tenant, Uuid7.NewGuid(), Uuid7.NewGuid(), 10m);
        // Old token no longer works…
        (await IngestClient(f, tenant.SyncToken).PostAsJsonAsync("/hq/sync/ingest", batch, PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // …the new one does.
        (await IngestClient(f, newToken).PostAsJsonAsync("/hq/sync/ingest", batch, PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ingest_rejects_a_bad_or_missing_token_and_unknown_tenant()
    {
        var f = HqFactory();
        var tenant = await Provision(f, Slug("acme"));
        var batch = SaleBatch(tenant, Uuid7.NewGuid(), Uuid7.NewGuid(), 10m);

        // Wrong token.
        (await IngestClient(f, "hqs_wrong").PostAsJsonAsync("/hq/sync/ingest", batch, PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Missing token.
        (await IngestClient(f, null).PostAsJsonAsync("/hq/sync/ingest", batch, PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Unknown tenant slug (valid-looking token, but no such tenant).
        var ghost = batch with { TenantSlug = Slug("ghost") };
        (await IngestClient(f, tenant.SyncToken).PostAsJsonAsync("/hq/sync/ingest", ghost, PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
