using FluentAssertions;
using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Identity;
using Pos.Application.Inventory;
using Pos.Application.Sync;
using Pos.Domain.Catalog;
using Pos.Domain.Identity;
using Pos.Domain.Inventory;
using Pos.Domain.Inventory.Events;
using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>M3 store-side units: dispatch writes a TransferOut + event; the destination receiver resolves
/// each line to its LOCAL product by SKU, applies TransferIn once (idempotent), and acks receipt.</summary>
public sealed class M3TransferUnitTests
{
    [Fact]
    public async Task Dispatch_writes_a_transfer_out_movement_and_raises_the_event()
    {
        var tenant = Uuid7.NewGuid(); var store = Uuid7.NewGuid(); var toStore = Uuid7.NewGuid(); var user = Uuid7.NewGuid();
        var product = Product.Create(tenant, store, "MILK", "Milk 500ml", new Money(60m, "KES"), UnitOfMeasure.Each, null, TaxClass.StandardRated);
        var transfers = new FakeTransfers();
        var movements = new FakeMovements();

        var svc = new TransferService(new FakeCtx(tenant, store, user, "Asha"), new FakeProducts(product), transfers, movements, new FakeUow());
        var t = await svc.DispatchAsync(toStore, "West Branch", new[] { new TransferLineInput(product.Id, 5m) }, "restock");

        t.FromStoreId.Should().Be(store);
        t.ToStoreId.Should().Be(toStore);
        t.Lines.Should().ContainSingle(l => l.Sku == "MILK" && l.Quantity == 5m);
        transfers.Added.Should().ContainSingle();
        movements.Recorded.Should().ContainSingle(m => m.Reason == StockMovementReason.TransferOut && m.QuantityDelta == -5m && m.ProductId == product.Id);
        t.DomainEvents.Should().ContainSingle(e => e is StockTransferDispatched);
    }

    [Fact]
    public async Task Dispatch_blocks_sending_more_than_on_hand()
    {
        var tenant = Uuid7.NewGuid(); var store = Uuid7.NewGuid(); var toStore = Uuid7.NewGuid();
        var product = Product.Create(tenant, store, "MILK", "Milk 500ml", new Money(60m, "KES"), UnitOfMeasure.Each, null, TaxClass.StandardRated);
        var movements = new FakeMovements { OnHand = 3m };
        var svc = new TransferService(new FakeCtx(tenant, store, Uuid7.NewGuid(), "Asha"), new FakeProducts(product), new FakeTransfers(), movements, new FakeUow());

        var act = () => svc.DispatchAsync(toStore, "West", new[] { new TransferLineInput(product.Id, 5m) }, null);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*on hand*");
        movements.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task Puller_stages_incoming_as_pending_without_moving_stock()
    {
        var tenant = Uuid7.NewGuid(); var store = Uuid7.NewGuid(); var fromStore = Uuid7.NewGuid(); var transferId = Uuid7.NewGuid();
        var snap = new TransferSnapshot(transferId, tenant, fromStore, store, "West", "Asha", DateTimeOffset.UtcNow, null,
            new[] { new TransferLineSnapshot(Uuid7.NewGuid(), "MILK", "Milk 500ml", 12m) });
        var client = new FakeTransferPull(new[] { snap });
        var incoming = new FakeIncoming();
        var puller = new IncomingTransferPuller(client, incoming,
            new StoreServerOptions { TenantId = tenant, StoreId = store }, new HqSyncOptions { Enabled = true }, new FakeUow());

        (await puller.RunOnceAsync()).Should().Be(1);
        var staged = incoming.Store.Should().ContainSingle(t => t.Id == transferId).Subject;
        staged.Status.Should().Be(IncomingTransferStatus.Pending);
        staged.Lines.Should().ContainSingle(l => l.Sku == "MILK" && l.ExpectedQuantity == 12m && l.ReceivedQuantity == null);
        client.Acked.Should().BeEmpty(); // pending → NOT acked yet (awaits the operator's count)

        // Re-pull while still pending: no duplicate row, still not acked.
        (await puller.RunOnceAsync()).Should().Be(0);
        incoming.Store.Should().ContainSingle();
        client.Acked.Should().BeEmpty();
    }

    [Fact]
    public async Task Receive_posts_the_counted_quantity_records_the_discrepancy_and_acks()
    {
        var tenant = Uuid7.NewGuid(); var store = Uuid7.NewGuid(); var fromStore = Uuid7.NewGuid(); var transferId = Uuid7.NewGuid();
        // The local product for MILK has a DIFFERENT id than the source's — resolution is by SKU.
        var localMilk = Product.Create(tenant, store, "MILK", "Milk 500ml", new Money(60m, "KES"), UnitOfMeasure.Each, null, TaxClass.StandardRated);
        var snap = new TransferSnapshot(transferId, tenant, fromStore, store, "West", "Asha", DateTimeOffset.UtcNow, null,
            new[] { new TransferLineSnapshot(Uuid7.NewGuid(), "MILK", "Milk 500ml", 12m) });

        var client = new FakeTransferPull(new[] { snap });
        var incoming = new FakeIncoming();
        var server = new StoreServerOptions { TenantId = tenant, StoreId = store };
        await new IncomingTransferPuller(client, incoming, server, new HqSyncOptions { Enabled = true }, new FakeUow()).RunOnceAsync();
        var lineId = incoming.Store.Single().Lines.Single().Id;

        var movements = new FakeMovements();
        var svc = new TransferReceivingService(new FakeCtx(tenant, store, Uuid7.NewGuid(), "Bila"), incoming,
            new FakeProducts(localMilk), movements, client, new FakeClock(), new FakeUow());

        // Only 10 of the 12 sent actually arrived.
        await svc.ReceiveAsync(transferId, new[] { new ReceiveLineInput(lineId, 10m) });

        movements.Recorded.Should().ContainSingle(m => m.Reason == StockMovementReason.TransferIn && m.QuantityDelta == 10m && m.ProductId == localMilk.Id);
        var t = incoming.Store.Single();
        t.Status.Should().Be(IncomingTransferStatus.Received);
        t.ReceivedByName.Should().Be("Bila");
        t.HasDiscrepancy.Should().BeTrue();
        t.Lines.Single().Discrepancy.Should().Be(-2m);
        client.Acked.Should().Contain(transferId);

        // Idempotent: re-receiving the same transfer is refused (no double-count).
        var act = () => svc.ReceiveAsync(transferId, new[] { new ReceiveLineInput(lineId, 10m) });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been received*");

        // And a re-pull of an already-received transfer just re-acks (a lost ack), never re-stages.
        client.Acked.Clear();
        (await new IncomingTransferPuller(client, incoming, server, new HqSyncOptions { Enabled = true }, new FakeUow()).RunOnceAsync()).Should().Be(0);
        client.Acked.Should().Contain(transferId);
    }

    // ── fakes ──
    private sealed class FakeCtx(Guid t, Guid s, Guid u, string name) : ICurrentContext
    {
        public Guid TenantId => t;
        public Guid StoreId => s;
        public Guid UserId => u;
        public UserRole Role => UserRole.Manager;
        public string UserName => name;
        public string StaffCode => "M1";
    }

    private sealed class FakeProducts(Product? p) : IProductRepository
    {
        public Task<Product?> GetAsync(Guid t, Guid s, Guid id, CancellationToken ct = default) =>
            Task.FromResult(p is not null && p.Id == id ? p : null);
        public Task<Product?> FindBySkuAsync(Guid t, Guid s, string sku, CancellationToken ct = default) =>
            Task.FromResult(p is not null && p.Sku == sku ? p : null);
        public Task<Product?> FindByBarcodeAsync(Guid t, Guid s, string bc, CancellationToken ct = default) => Task.FromResult<Product?>(null);
        public Task<IReadOnlyList<Product>> ListAsync(Guid t, Guid s, bool inc = false, Guid? cat = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Product>>([]);
        public Task AddAsync(Product x, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<Guid, Guid?>> GetCategoryMapAsync(Guid t, IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<Guid, Guid?>>(new Dictionary<Guid, Guid?>());
        public Task<bool> SkuExistsAsync(Guid t, string sku, Guid? ex = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> BarcodeExistsAsync(Guid t, string bc, Guid? ex = null, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class FakeTransfers : ITransferRepository
    {
        public List<StockTransfer> Added { get; } = new();
        public Task AddAsync(StockTransfer t, CancellationToken ct = default) { Added.Add(t); return Task.CompletedTask; }
        public Task<StockTransfer?> GetAsync(Guid t, Guid s, Guid id, CancellationToken ct = default) => Task.FromResult<StockTransfer?>(null);
        public Task<IReadOnlyList<StockTransfer>> ListRecentAsync(Guid t, Guid s, int take, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StockTransfer>>([]);
    }

    private sealed class FakeMovements : IStockMovementRepository
    {
        public List<StockMovement> Recorded { get; } = new();
        public decimal OnHand { get; set; } = 1000m;
        public Task AddAsync(StockMovement m, CancellationToken ct = default) { Recorded.Add(m); return Task.CompletedTask; }
        public Task AddRangeAsync(IEnumerable<StockMovement> ms, CancellationToken ct = default) { Recorded.AddRange(ms); return Task.CompletedTask; }
        public Task<decimal> GetOnHandAsync(Guid t, Guid s, Guid p, CancellationToken ct = default) => Task.FromResult(OnHand);
        public Task<IReadOnlyDictionary<Guid, decimal>> GetOnHandByProductAsync(Guid t, Guid s, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }

    private sealed class FakeIncoming : IIncomingTransferRepository
    {
        public List<IncomingTransfer> Store { get; } = new();
        public Task<IncomingTransfer?> GetAsync(Guid t, Guid s, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(x => x.TenantId == t && x.StoreId == s && x.Id == id));
        public Task<bool> ExistsAsync(Guid t, Guid s, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Store.Any(x => x.TenantId == t && x.StoreId == s && x.Id == id));
        public Task<IReadOnlyList<IncomingTransfer>> ListPendingAsync(Guid t, Guid s, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IncomingTransfer>>(Store.Where(x => x.TenantId == t && x.StoreId == s && x.Status == IncomingTransferStatus.Pending).ToList());
        public Task<IReadOnlyList<IncomingTransfer>> ListRecentAsync(Guid t, Guid s, int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IncomingTransfer>>(Store.Where(x => x.TenantId == t && x.StoreId == s).ToList());
        public Task AddAsync(IncomingTransfer m, CancellationToken ct = default) { Store.Add(m); return Task.CompletedTask; }
    }

    private sealed class FakeTransferPull(IReadOnlyList<TransferSnapshot> incoming) : IHqTransferPullClient
    {
        public List<Guid> Acked { get; } = new();
        public Task<IReadOnlyList<TransferSnapshot>> IncomingAsync(CancellationToken ct = default) => Task.FromResult(incoming);
        public Task AckReceivedAsync(Guid id, CancellationToken ct = default) { Acked.Add(id); return Task.CompletedTask; }
        public Task<IReadOnlyList<BranchDto>> BranchesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BranchDto>>([]);
    }

    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

    private sealed class FakeUow : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) => work(ct);
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default) => work(ct);
    }
}
