using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Identity;
using Pos.Application.Inventory;
using Pos.Domain.Inventory;

namespace Pos.Application.Sync;

/// <summary>Port: pulls this store's incoming transfers from HQ and acks receipt.</summary>
public interface IHqTransferPullClient
{
    Task<IReadOnlyList<TransferSnapshot>> IncomingAsync(CancellationToken ct = default);
    Task AckReceivedAsync(Guid transferId, CancellationToken ct = default);
    /// <summary>The tenant's OTHER branches (for the dispatch destination picker). Empty on failure.</summary>
    Task<IReadOnlyList<BranchDto>> BranchesAsync(CancellationToken ct = default);
}

/// <summary>
/// Destination side of inter-branch transfers (M3): pull the transfers HQ has routed to this store, write
/// a <see cref="StockMovementReason.TransferIn"/> per line (resolved to the LOCAL product by SKU, since
/// products are store-scoped), record a dedup marker, then ack receipt to HQ. Apply + marker commit
/// atomically; the ack follows — if it's lost, the marker stops a re-pull from double-incrementing.
/// </summary>
public sealed class HqTransferReceiver
{
    private readonly IHqTransferPullClient _client;
    private readonly IReceivedTransferRepository _received;
    private readonly IProductRepository _products;
    private readonly IStockMovementRepository _movements;
    private readonly StoreServerOptions _server;
    private readonly HqSyncOptions _options;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public HqTransferReceiver(IHqTransferPullClient client, IReceivedTransferRepository received,
        IProductRepository products, IStockMovementRepository movements, StoreServerOptions server,
        HqSyncOptions options, IClock clock, IUnitOfWork uow)
    {
        _client = client;
        _received = received;
        _products = products;
        _movements = movements;
        _server = server;
        _options = options;
        _clock = clock;
        _uow = uow;
    }

    /// <summary>Returns the number of transfers newly applied this pass.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled) return 0;
        var tenant = _server.TenantId;
        var store = _server.StoreId;
        if (tenant == Guid.Empty || store == Guid.Empty) return 0;

        var incoming = await _client.IncomingAsync(ct);
        if (incoming.Count == 0) return 0;

        var applied = 0;
        foreach (var t in incoming)
        {
            if (await _received.ExistsAsync(tenant, store, t.TransferId, ct))
            {
                await _client.AckReceivedAsync(t.TransferId, ct); // already applied; just (re)confirm receipt
                continue;
            }

            var movements = new List<StockMovement>(t.Lines.Count);
            foreach (var line in t.Lines)
            {
                var product = await _products.FindBySkuAsync(tenant, store, line.Sku, ct);
                if (product is null) continue; // SKU not in this branch's catalogue (HQ catalogue push should seed it)
                movements.Add(StockMovement.Record(tenant, store, product.Id, line.Quantity,
                    StockMovementReason.TransferIn, sourceRef: t.TransferId));
            }

            if (movements.Count > 0) await _movements.AddRangeAsync(movements, ct);
            await _received.AddAsync(ReceivedTransfer.Mark(tenant, store, t.TransferId, _clock.UtcNow), ct);
            await _uow.SaveChangesAsync(ct);            // TransferIn + marker, atomic

            await _client.AckReceivedAsync(t.TransferId, ct); // then HQ marks it Received
            applied++;
        }
        return applied;
    }
}
