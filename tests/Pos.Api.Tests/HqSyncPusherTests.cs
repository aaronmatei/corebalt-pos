using System.Text.Json;
using FluentAssertions;
using Pos.Application.Cash;
using Pos.Application.Catalog;
using Pos.Application.Identity;
using Pos.Application.Inventory;
using Pos.Application.Sales;
using Pos.Application.Sync;
using Pos.Application.Tenancy;
using Pos.Domain.Tenancy;
using Pos.Domain.Catalog;
using Pos.Domain.Cash;
using Pos.Domain.Cash.Events;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;
using Pos.Domain.Sales.Events;
using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// On-prem store→cloud push: HqSyncPusher reads its store's unprocessed outbox, hydrates a SaleCompleted
/// into a full snapshot, ships the batch, and acks exactly what the cloud accepted. Pure unit test over
/// the ports — no DB / no host.
/// </summary>
public sealed class HqSyncPusherTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Pushes_a_hydrated_sale_snapshot_and_acks_what_the_cloud_accepts()
    {
        var tenant = Uuid7.NewGuid();
        var store = Uuid7.NewGuid();
        var saleId = Uuid7.NewGuid();
        var changeId = Uuid7.NewGuid();

        var sale = Sale.Start(tenant, store, Uuid7.NewGuid(), Uuid7.NewGuid(), "KES",
            cashierName: "Jane Cashier", cashierStaffCode: "J01", registerName: "Lane 1",
            registerSessionId: Uuid7.NewGuid(), saleId: saleId);
        sale.AddLine(Uuid7.NewGuid(), "Sugar 1kg", 2m, new Money(100m, "KES"), TaxClass.StandardRated);
        sale.AddTender(TenderType.Cash, new Money(200m, "KES"));
        sale.AssignReceiptNumber("MB-000123");
        sale.Complete();

        var change = new OutboxChange(changeId, tenant, store, saleId, typeof(SaleCompleted).FullName!,
            "{}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var outbox = new FakeOutbox(change);
        var sales = new FakeSales(sale);
        var client = new FakeClient(acceptAll: true);
        var pusher = new HqSyncPusher(outbox, sales, new FakeSessions(), new FakeCreditNotes(), new FakeProducts(), new FakeTransfers(), new FakeMerchants(), client,
            new StoreServerOptions { TenantId = tenant, StoreId = store },
            new HqSyncOptions { Enabled = true, TenantSlug = "acme", BatchSize = 100 });

        var acked = await pusher.RunOnceAsync();

        acked.Should().Be(1);
        client.LastRequest.Should().NotBeNull();
        client.LastRequest!.TenantSlug.Should().Be("acme");
        var pushed = client.LastRequest.Changes.Should().ContainSingle().Subject;
        pushed.Id.Should().Be(changeId);
        pushed.Snapshot.Should().NotBeNull("a SaleCompleted change ships a hydrated snapshot");

        var snap = JsonSerializer.Deserialize<SaleSnapshot>(pushed.Snapshot!, Json)!;
        snap.SaleId.Should().Be(saleId);
        snap.GrandTotal.Should().Be(200m);
        snap.ReceiptNumber.Should().Be("MB-000123");
        snap.Lines.Should().ContainSingle().Which.Description.Should().Be("Sugar 1kg");

        outbox.Acked.Should().ContainSingle().Which.Should().Be(changeId);
    }

    [Fact]
    public async Task Pushes_a_hydrated_session_snapshot()
    {
        var tenant = Uuid7.NewGuid();
        var store = Uuid7.NewGuid();
        var changeId = Uuid7.NewGuid();

        var session = RegisterSession.Open(tenant, store, Uuid7.NewGuid(), "Lane 1",
            Uuid7.NewGuid(), "Asha Manager", new Money(1000m, "KES"));
        session.Close(Uuid7.NewGuid(), "Asha Manager", new Money(5000m, "KES"), new Money(5100m, "KES"), varianceAcknowledged: true);

        var change = new OutboxChange(changeId, tenant, store, session.Id, typeof(RegisterSessionClosed).FullName!,
            "{}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var client = new FakeClient(acceptAll: true);
        var pusher = new HqSyncPusher(new FakeOutbox(change), new FakeSales(), new FakeSessions(session), new FakeCreditNotes(), new FakeProducts(), new FakeTransfers(), new FakeMerchants(), client,
            new StoreServerOptions { TenantId = tenant, StoreId = store },
            new HqSyncOptions { Enabled = true, TenantSlug = "acme", BatchSize = 100 });

        (await pusher.RunOnceAsync()).Should().Be(1);
        var pushed = client.LastRequest!.Changes.Should().ContainSingle().Subject;
        pushed.Snapshot.Should().NotBeNull();
        var snap = JsonSerializer.Deserialize<SessionSnapshot>(pushed.Snapshot!, Json)!;
        snap.SessionId.Should().Be(session.Id);
        snap.RegisterLabel.Should().Be("Lane 1");
        snap.ExpectedCash.Should().Be(5100m);
        snap.CountedCash.Should().Be(5000m);
        snap.Variance.Should().Be(-100m); // counted − expected
    }

    [Fact]
    public async Task Ships_a_stock_movement_built_from_the_event_payload()
    {
        var tenant = Uuid7.NewGuid();
        var store = Uuid7.NewGuid();
        var productId = Uuid7.NewGuid();
        var movementId = Uuid7.NewGuid();
        var changeId = Uuid7.NewGuid();

        // The outbox payload IS the serialized domain event (Web defaults, as the interceptor writes it).
        var evt = new Pos.Domain.Inventory.Events.StockMovementRecorded(
            movementId, tenant, store, productId, -2m, StockMovementReason.Sale);
        var payload = JsonSerializer.Serialize(evt, Json);
        var change = new OutboxChange(changeId, tenant, store, movementId,
            typeof(Pos.Domain.Inventory.Events.StockMovementRecorded).FullName!, payload, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var client = new FakeClient(acceptAll: true);
        var pusher = new HqSyncPusher(new FakeOutbox(change), new FakeSales(), new FakeSessions(), new FakeCreditNotes(), new FakeProducts(), new FakeTransfers(), new FakeMerchants(), client,
            new StoreServerOptions { TenantId = tenant, StoreId = store },
            new HqSyncOptions { Enabled = true, TenantSlug = "acme", BatchSize = 100 });

        (await pusher.RunOnceAsync()).Should().Be(1);
        var pushed = client.LastRequest!.Changes.Should().ContainSingle().Subject;
        pushed.Snapshot.Should().NotBeNull();
        var snap = JsonSerializer.Deserialize<StockMovementSnapshot>(pushed.Snapshot!, Json)!;
        snap.ProductId.Should().Be(productId);
        snap.QuantityDelta.Should().Be(-2m);
        snap.Reason.Should().Be("Sale");
    }

    [Fact]
    public async Task Does_nothing_when_disabled()
    {
        var outbox = new FakeOutbox();
        var pusher = new HqSyncPusher(outbox, new FakeSales(), new FakeSessions(), new FakeCreditNotes(), new FakeProducts(), new FakeTransfers(), new FakeMerchants(), new FakeClient(true),
            new StoreServerOptions { TenantId = Uuid7.NewGuid(), StoreId = Uuid7.NewGuid() },
            new HqSyncOptions { Enabled = false });

        (await pusher.RunOnceAsync()).Should().Be(0);
        outbox.ReadCalls.Should().Be(0);
    }

    // ── fakes ──
    private sealed class FakeOutbox(params OutboxChange[] changes) : IOutboxSyncStore
    {
        private readonly List<OutboxChange> _changes = changes.ToList();
        public int ReadCalls { get; private set; }
        public List<Guid> Acked { get; } = new();

        public Task<IReadOnlyList<OutboxChange>> ReadUnprocessedAsync(Guid tenantId, Guid storeId, int max, CancellationToken ct = default)
        {
            ReadCalls++;
            return Task.FromResult<IReadOnlyList<OutboxChange>>(_changes.Take(max).ToList());
        }

        public Task<int> CountUnprocessedAsync(Guid tenantId, Guid storeId, CancellationToken ct = default) =>
            Task.FromResult(_changes.Count);

        public Task<int> AcknowledgeAsync(Guid tenantId, Guid storeId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
        {
            var hit = _changes.Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToList();
            Acked.AddRange(hit);
            _changes.RemoveAll(c => hit.Contains(c.Id));
            return Task.FromResult(hit.Count);
        }
    }

    private sealed class FakeSales(Sale? sale = null) : ISaleRepository
    {
        public Task<Sale?> GetAsync(Guid tenantId, Guid storeId, Guid saleId, CancellationToken ct = default) =>
            Task.FromResult(sale is not null && sale.Id == saleId ? sale : null);
        public Task AddAsync(Sale s, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Sale>> ListBySessionAsync(Guid t, Guid s, Guid sid, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Sale>>([]);
        public Task<IReadOnlyList<Sale>> ListCompletedBetweenAsync(Guid t, Guid s, DateTimeOffset f, DateTimeOffset to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Sale>>([]);
        public Task<IReadOnlyList<Sale>> ListByFiscalStatusAsync(FiscalStatus status, int max, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Sale>>([]);
    }

    private sealed class FakeSessions(RegisterSession? session = null) : IRegisterSessionRepository
    {
        public Task<RegisterSession?> GetAsync(Guid t, Guid s, Guid id, CancellationToken ct = default) =>
            Task.FromResult(session is not null && session.Id == id ? session : null);
        public Task<RegisterSession?> GetOpenAsync(Guid t, Guid s, Guid reg, CancellationToken ct = default) =>
            Task.FromResult<RegisterSession?>(null);
        public Task AddAsync(RegisterSession s, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<RegisterSession>> ListAsync(Guid t, Guid s, DateTimeOffset f, DateTimeOffset to, Guid? reg, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RegisterSession>>([]);
    }

    private sealed class FakeCreditNotes(CreditNote? note = null) : ICreditNoteRepository
    {
        public Task<CreditNote?> GetAsync(Guid t, Guid s, Guid id, CancellationToken ct = default) =>
            Task.FromResult(note is not null && note.Id == id ? note : null);
        public Task AddAsync(CreditNote c, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<Guid, decimal>> GetReturnedQuantitiesAsync(Guid t, Guid s, Guid sale, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
        public Task<IReadOnlyList<CreditNote>> ListBySessionAsync(Guid t, Guid s, Guid sid, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CreditNote>>([]);
        public Task<IReadOnlyList<CreditNote>> ListBetweenAsync(Guid t, Guid s, DateTimeOffset f, DateTimeOffset to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CreditNote>>([]);
    }

    private sealed class FakeProducts(Product? product = null) : IProductRepository
    {
        public Task<Product?> GetAsync(Guid t, Guid s, Guid id, CancellationToken ct = default) =>
            Task.FromResult(product is not null && product.Id == id ? product : null);
        public Task<Product?> FindBySkuAsync(Guid t, Guid s, string sku, CancellationToken ct = default) => Task.FromResult<Product?>(null);
        public Task<Product?> FindByBarcodeAsync(Guid t, Guid s, string bc, CancellationToken ct = default) => Task.FromResult<Product?>(null);
        public Task<IReadOnlyList<Product>> ListAsync(Guid t, Guid s, bool inc = false, Guid? cat = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Product>>([]);
        public Task AddAsync(Product p, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<Guid, Guid?>> GetCategoryMapAsync(Guid t, IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Guid?>>(new Dictionary<Guid, Guid?>());
        public Task<bool> SkuExistsAsync(Guid t, string sku, Guid? ex = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> BarcodeExistsAsync(Guid t, string bc, Guid? ex = null, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class FakeTransfers : ITransferRepository
    {
        public Task AddAsync(StockTransfer t, CancellationToken ct = default) => Task.CompletedTask;
        public Task<StockTransfer?> GetAsync(Guid t, Guid s, Guid id, CancellationToken ct = default) => Task.FromResult<StockTransfer?>(null);
        public Task<IReadOnlyList<StockTransfer>> ListRecentAsync(Guid t, Guid s, int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StockTransfer>>([]);
    }

    private sealed class FakeMerchants : IMerchantProfileRepository
    {
        public Task<MerchantProfile?> GetAsync(Guid t, CancellationToken ct = default) => Task.FromResult<MerchantProfile?>(null);
        public Task AddAsync(MerchantProfile p, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeClient(bool acceptAll) : IHqSyncClient
    {
        public SyncIngestRequest? LastRequest { get; private set; }
        public Task<SyncIngestResponse> PushAsync(SyncIngestRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            var ids = acceptAll ? request.Changes.Select(c => c.Id).ToList() : new List<Guid>();
            return Task.FromResult(new SyncIngestResponse(ids));
        }
    }
}
