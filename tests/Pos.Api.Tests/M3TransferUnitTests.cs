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
    public async Task Receiver_applies_transfer_in_by_sku_once_and_acks()
    {
        var tenant = Uuid7.NewGuid(); var store = Uuid7.NewGuid(); var fromStore = Uuid7.NewGuid(); var transferId = Uuid7.NewGuid();
        // The local product for MILK has a DIFFERENT id than the source's — resolution is by SKU.
        var localMilk = Product.Create(tenant, store, "MILK", "Milk 500ml", new Money(60m, "KES"), UnitOfMeasure.Each, null, TaxClass.StandardRated);
        var snap = new TransferSnapshot(transferId, tenant, fromStore, store, "West", "Asha", DateTimeOffset.UtcNow, null,
            new[] { new TransferLineSnapshot(Uuid7.NewGuid(), "MILK", "Milk 500ml", 12m) });

        var client = new FakeTransferPull(new[] { snap });
        var received = new FakeReceived();
        var movements = new FakeMovements();
        var receiver = new HqTransferReceiver(client, received, new FakeProducts(localMilk), movements,
            new StoreServerOptions { TenantId = tenant, StoreId = store }, new HqSyncOptions { Enabled = true }, new FakeClock(), new FakeUow());

        (await receiver.RunOnceAsync()).Should().Be(1);
        movements.Recorded.Should().ContainSingle(m => m.Reason == StockMovementReason.TransferIn && m.QuantityDelta == 12m && m.ProductId == localMilk.Id);
        received.Added.Should().ContainSingle(r => r.TransferId == transferId);
        client.Acked.Should().Contain(transferId);

        // Idempotent: already received → no new movement, just re-ack.
        received.AlreadyReceived = true;
        movements.Recorded.Clear();
        client.Acked.Clear();
        (await receiver.RunOnceAsync()).Should().Be(0);
        movements.Recorded.Should().BeEmpty();
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
        public Task AddAsync(StockMovement m, CancellationToken ct = default) { Recorded.Add(m); return Task.CompletedTask; }
        public Task AddRangeAsync(IEnumerable<StockMovement> ms, CancellationToken ct = default) { Recorded.AddRange(ms); return Task.CompletedTask; }
        public Task<decimal> GetOnHandAsync(Guid t, Guid s, Guid p, CancellationToken ct = default) => Task.FromResult(0m);
        public Task<IReadOnlyDictionary<Guid, decimal>> GetOnHandByProductAsync(Guid t, Guid s, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }

    private sealed class FakeReceived : IReceivedTransferRepository
    {
        public bool AlreadyReceived { get; set; }
        public List<ReceivedTransfer> Added { get; } = new();
        public Task<bool> ExistsAsync(Guid t, Guid s, Guid id, CancellationToken ct = default) => Task.FromResult(AlreadyReceived);
        public Task AddAsync(ReceivedTransfer m, CancellationToken ct = default) { Added.Add(m); return Task.CompletedTask; }
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
