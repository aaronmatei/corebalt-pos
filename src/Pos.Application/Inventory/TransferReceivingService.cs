using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Sync;
using Pos.Domain.Inventory;

namespace Pos.Application.Inventory;

public sealed record ReceiveLineInput(Guid LineId, decimal CountedQuantity);

/// <summary>
/// Destination-branch receipt of an inter-branch transfer (M3): an operator confirms what physically arrived,
/// line by line. The COUNTED quantity (not the dispatched quantity) is what posts as
/// <see cref="StockMovementReason.TransferIn"/> — so a short/over delivery self-corrects and the discrepancy
/// is recorded on the <see cref="IncomingTransfer"/>. Apply + flip-to-Received commit atomically; the HQ ack
/// follows (idempotent — the puller re-acks if it's lost). Products resolve to the LOCAL catalogue by SKU.
/// </summary>
public sealed class TransferReceivingService
{
    private readonly ICurrentContext _ctx;
    private readonly IIncomingTransferRepository _incoming;
    private readonly IProductRepository _products;
    private readonly IStockMovementRepository _movements;
    private readonly IHqTransferPullClient _client;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public TransferReceivingService(ICurrentContext ctx, IIncomingTransferRepository incoming,
        IProductRepository products, IStockMovementRepository movements, IHqTransferPullClient client,
        IClock clock, IUnitOfWork uow)
    {
        _ctx = ctx;
        _incoming = incoming;
        _products = products;
        _movements = movements;
        _client = client;
        _clock = clock;
        _uow = uow;
    }

    public Task<IReadOnlyList<IncomingTransfer>> ListPendingAsync(CancellationToken ct = default) =>
        _incoming.ListPendingAsync(_ctx.TenantId, _ctx.StoreId, ct);

    public Task<IReadOnlyList<IncomingTransfer>> ListRecentAsync(int take = 50, CancellationToken ct = default) =>
        _incoming.ListRecentAsync(_ctx.TenantId, _ctx.StoreId, take, ct);

    public async Task ReceiveAsync(Guid transferId, IReadOnlyList<ReceiveLineInput> counts, CancellationToken ct = default)
    {
        var tenant = _ctx.TenantId;
        var store = _ctx.StoreId;

        var transfer = await _incoming.GetAsync(tenant, store, transferId, ct)
            ?? throw new InvalidOperationException("That incoming transfer was not found.");
        if (transfer.Status == IncomingTransferStatus.Received)
            throw new InvalidOperationException("That transfer has already been received.");

        var countMap = counts.GroupBy(c => c.LineId).ToDictionary(g => g.Key, g => g.Last().CountedQuantity);

        await _uow.ExecuteInTransactionAsync(async inner =>
        {
            transfer.Receive(countMap, _ctx.UserName, _clock.UtcNow);

            var moves = new List<StockMovement>(transfer.Lines.Count);
            foreach (var line in transfer.Lines)
            {
                var qty = line.ReceivedQuantity ?? 0m;
                if (qty <= 0) continue; // nothing physically arrived for this line — no movement
                var product = await _products.FindBySkuAsync(tenant, store, line.Sku, inner);
                if (product is null) continue; // SKU not in this branch's catalogue (the HQ catalogue push seeds it)
                moves.Add(StockMovement.Record(tenant, store, product.Id, qty,
                    StockMovementReason.TransferIn, sourceRef: transferId));
            }
            if (moves.Count > 0) await _movements.AddRangeAsync(moves, inner);
            await _uow.SaveChangesAsync(inner); // TransferIn movements + the Received flip, atomic
        }, ct);

        // Tell HQ it's received so it stops routing it here. Best-effort: if this is lost, the puller re-acks.
        try { await _client.AckReceivedAsync(transferId, ct); }
        catch { /* re-acked on the next pull pass */ }
    }
}
