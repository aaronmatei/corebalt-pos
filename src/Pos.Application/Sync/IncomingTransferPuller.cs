using Pos.Application.Abstractions;
using Pos.Application.Identity;
using Pos.Application.Inventory;
using Pos.Domain.Inventory;

namespace Pos.Application.Sync;

/// <summary>Port: pulls this store's incoming transfers from HQ and acks receipt.</summary>
public interface IHqTransferPullClient
{
    Task<IReadOnlyList<TransferSnapshot>> IncomingAsync(CancellationToken ct = default);
    Task AckReceivedAsync(Guid transferId, CancellationToken ct = default);
    /// <summary>The tenant's OTHER branches (for the dispatch destination picker + source-branch labels). Empty on failure.</summary>
    Task<IReadOnlyList<BranchDto>> BranchesAsync(CancellationToken ct = default);
}

/// <summary>
/// Destination side of inter-branch transfers (M3), pull half: it STAGES each transfer HQ has routed to this
/// store as a Pending <see cref="IncomingTransfer"/> — it does NOT move stock and does NOT ack. Stock is
/// applied later, with an operator-entered counted quantity, by <see cref="TransferReceivingService"/>. The
/// local row is the dedup marker: a transfer already staged is left alone (awaiting its count); one already
/// received is simply re-acked in case the prior ack was lost. Idempotent and safe to re-run every interval.
/// </summary>
public sealed class IncomingTransferPuller
{
    private readonly IHqTransferPullClient _client;
    private readonly IIncomingTransferRepository _incoming;
    private readonly StoreServerOptions _server;
    private readonly HqSyncOptions _options;
    private readonly IUnitOfWork _uow;

    public IncomingTransferPuller(IHqTransferPullClient client, IIncomingTransferRepository incoming,
        StoreServerOptions server, HqSyncOptions options, IUnitOfWork uow)
    {
        _client = client;
        _incoming = incoming;
        _server = server;
        _options = options;
        _uow = uow;
    }

    /// <summary>Returns the number of transfers newly STAGED (awaiting receipt) this pass.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled) return 0;
        var tenant = _server.TenantId;
        var store = _server.StoreId;
        if (tenant == Guid.Empty || store == Guid.Empty) return 0;

        var incoming = await _client.IncomingAsync(ct);
        if (incoming.Count == 0) return 0;

        // Best-effort source-branch labels (degrades to the bare id if HQ's branch list is unreachable).
        IReadOnlyDictionary<Guid, string> branchNames;
        try { branchNames = (await _client.BranchesAsync(ct)).ToDictionary(b => b.StoreId, b => b.Name); }
        catch { branchNames = new Dictionary<Guid, string>(); }

        var staged = 0;
        foreach (var t in incoming)
        {
            var existing = await _incoming.GetAsync(tenant, store, t.TransferId, ct);
            if (existing is not null)
            {
                if (existing.Status == IncomingTransferStatus.Received)
                    await _client.AckReceivedAsync(t.TransferId, ct); // a lost ack — re-confirm so HQ stops resending
                continue; // Pending → leave it for the operator to count
            }

            var fromName = branchNames.TryGetValue(t.FromStoreId, out var n) && !string.IsNullOrWhiteSpace(n)
                ? n : "another branch";
            var lines = t.Lines.Select(l => (l.Sku, l.Name, l.Quantity));
            await _incoming.AddAsync(IncomingTransfer.Stage(tenant, store, t.TransferId, t.FromStoreId,
                fromName, t.DispatchedByName, t.DispatchedAtUtc, t.Note, lines), ct);
            await _uow.SaveChangesAsync(ct);
            staged++;
        }
        return staged;
    }
}
