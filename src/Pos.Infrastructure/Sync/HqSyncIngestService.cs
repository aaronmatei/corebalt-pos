using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Abstractions;
using Pos.Application.Sync;
using Pos.Domain.Cash.Events;
using Pos.Domain.Hq;
using Pos.Domain.Inventory.Events;
using Pos.Domain.Sales.Events;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Sync;

/// <summary>
/// Durably stores received changes in <c>sync_inbox</c> (idempotent on the original change id) and
/// projects the ones it understands. v1 projects <see cref="SaleCompleted"/> from its hydrated
/// <see cref="SaleSnapshot"/> into the <c>hq_sales</c> read-model. Everything is written under the
/// VERIFIED tenant id (the client's claimed slug/store is never trusted for ownership).
/// </summary>
internal sealed class HqSyncIngestService : IHqSyncIngestService
{
    private static readonly string SaleCompletedType = typeof(SaleCompleted).FullName!;
    private static readonly string SessionClosedType = typeof(RegisterSessionClosed).FullName!;
    private static readonly string CreditNoteIssuedType = typeof(CreditNoteIssued).FullName!;
    private static readonly string StockMovementType = typeof(StockMovementRecorded).FullName!;
    private static readonly string TransferDispatchedType = typeof(StockTransferDispatched).FullName!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PosDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<HqSyncIngestService> _log;

    public HqSyncIngestService(PosDbContext db, IClock clock, ILogger<HqSyncIngestService> log)
    {
        _db = db;
        _clock = clock;
        _log = log;
    }

    public async Task<SyncIngestResponse> IngestAsync(Guid tenantId, SyncIngestRequest request, CancellationToken ct = default)
    {
        var changes = request.Changes ?? [];
        if (changes.Count == 0) return new SyncIngestResponse([]);

        var now = _clock.UtcNow;

        // M3: keep the HQ branch registry fresh from the store's self-reported branch name.
        if (!string.IsNullOrWhiteSpace(request.StoreName) && request.StoreId != Guid.Empty)
        {
            var branch = await _db.HqBranches.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.StoreId == request.StoreId, ct);
            if (branch is null) _db.HqBranches.Add(HqBranch.Create(tenantId, request.StoreId, request.StoreName!, now));
            else branch.Seen(request.StoreName!, now);
        }

        var ids = changes.Select(c => c.Id).ToList();

        // No tenant query filter on this path (token-authed, not principal-scoped), so scope every read by
        // the VERIFIED tenant id explicitly. Idempotency: already-inboxed changes are accepted again with
        // no second write; already-projected sales are updated in place (re-projection is an upsert).
        var known = (await _db.SyncInbox
            .Where(e => e.TenantId == tenantId && ids.Contains(e.Id))
            .Select(e => e.Id).ToListAsync(ct)).ToHashSet();

        var saleIds = changes.Where(c => c.EventType == SaleCompletedType).Select(c => c.AggregateId).Distinct().ToList();
        var existingSales = saleIds.Count == 0
            ? new Dictionary<Guid, HqSale>()
            : await _db.HqSales.Where(s => s.TenantId == tenantId && saleIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        var sessionIds = changes.Where(c => c.EventType == SessionClosedType).Select(c => c.AggregateId).Distinct().ToList();
        var existingSessions = sessionIds.Count == 0
            ? new Dictionary<Guid, HqSession>()
            : await _db.HqSessions.Where(s => s.TenantId == tenantId && sessionIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        var noteIds = changes.Where(c => c.EventType == CreditNoteIssuedType).Select(c => c.AggregateId).Distinct().ToList();
        var existingNotes = noteIds.Count == 0
            ? new Dictionary<Guid, HqCreditNote>()
            : await _db.HqCreditNotes.Where(c => c.TenantId == tenantId && noteIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        // Stock-on-hand is keyed by (store, product); preload the rows the batch's movements will touch.
        var productIds = ExtractStockProductIds(changes);
        var existingStock = productIds.Count == 0
            ? new Dictionary<(Guid Store, Guid Product), HqStockOnHand>()
            : (await _db.HqStockOnHand.Where(s => s.TenantId == tenantId && productIds.Contains(s.ProductId)).ToListAsync(ct))
                .ToDictionary(s => (s.StoreId, s.ProductId));

        var transferIds = changes.Where(c => c.EventType == TransferDispatchedType).Select(c => c.AggregateId).Distinct().ToList();
        var existingTransfers = transferIds.Count == 0
            ? new Dictionary<Guid, HqTransfer>()
            : await _db.HqTransfers.Where(t => t.TenantId == tenantId && transferIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);

        var accepted = new List<Guid>(changes.Count);
        foreach (var change in changes)
        {
            accepted.Add(change.Id); // durably accepted either way (already-known or stored below)
            if (known.Contains(change.Id)) continue;

            var entry = SyncInboxEntry.Receive(change.Id, tenantId, request.StoreId, change.AggregateId,
                change.EventType, change.Payload, change.Snapshot, change.OccurredAtUtc, change.EnqueuedAtUtc, now);

            var projected = change.EventType switch
            {
                _ when change.EventType == SaleCompletedType => ProjectSale(tenantId, change, existingSales, now),
                _ when change.EventType == SessionClosedType => ProjectSession(tenantId, change, existingSessions, now),
                _ when change.EventType == CreditNoteIssuedType => ProjectCreditNote(tenantId, change, existingNotes, now),
                _ when change.EventType == StockMovementType => ProjectStock(tenantId, change, existingStock, now),
                _ when change.EventType == TransferDispatchedType => ProjectTransfer(tenantId, change, existingTransfers, now),
                _ => false,
            };
            if (projected) entry.MarkProjected(now);

            _db.SyncInbox.Add(entry);
        }

        await _db.SaveChangesAsync(ct);
        return new SyncIngestResponse(accepted);
    }

    /// <summary>Upsert the hq_sales read-model from a SaleCompleted snapshot. Returns false (leaving the
    /// inbox row unprojected) on a missing/garbled snapshot so a poison message never wedges the batch.</summary>
    private bool ProjectSale(Guid tenantId, SyncChangeDto change, Dictionary<Guid, HqSale> existing, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(change.Snapshot)) return false;
        SaleSnapshot? snap;
        try { snap = JsonSerializer.Deserialize<SaleSnapshot>(change.Snapshot!, Json); }
        catch (JsonException ex) { _log.LogWarning(ex, "hq.sync.project bad sale snapshot for change {Id}", change.Id); return false; }
        if (snap is null) return false;

        var linesJson = JsonSerializer.Serialize(snap.Lines, Json);
        if (existing.TryGetValue(snap.SaleId, out var sale))
        {
            sale.Apply(tenantId, sale.StoreId, snap.ReceiptNumber, snap.RegisterName, snap.CashierName,
                snap.CustomerId, snap.Currency, snap.GrandTotal, snap.TotalVat, snap.Lines.Count, linesJson,
                snap.CompletedAtUtc, now);
        }
        else
        {
            var created = HqSale.Create(snap.SaleId, tenantId, snap.StoreId, snap.ReceiptNumber,
                snap.RegisterName, snap.CashierName, snap.CustomerId, snap.Currency, snap.GrandTotal,
                snap.TotalVat, snap.Lines.Count, linesJson, snap.CompletedAtUtc, now);
            _db.HqSales.Add(created);
            existing[snap.SaleId] = created; // a duplicate sale later in the same batch updates this instance
        }
        return true;
    }

    /// <summary>Upsert the hq_sessions read-model from a RegisterSessionClosed snapshot.</summary>
    private bool ProjectSession(Guid tenantId, SyncChangeDto change, Dictionary<Guid, HqSession> existing, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(change.Snapshot)) return false;
        SessionSnapshot? snap;
        try { snap = JsonSerializer.Deserialize<SessionSnapshot>(change.Snapshot!, Json); }
        catch (JsonException ex) { _log.LogWarning(ex, "hq.sync.project bad session snapshot for change {Id}", change.Id); return false; }
        if (snap is null) return false;

        if (!existing.TryGetValue(snap.SessionId, out var session))
        {
            session = HqSession.Create(snap.SessionId, now);
            _db.HqSessions.Add(session);
            existing[snap.SessionId] = session;
        }
        session.Apply(tenantId, snap.StoreId, snap.RegisterId, snap.RegisterLabel, snap.OpenedByName,
            snap.OpenedAtUtc, snap.OpeningFloat, snap.ClosedByName, snap.ClosedAtUtc, snap.CountedCash,
            snap.ExpectedCash, snap.Variance, snap.VarianceAcknowledged, snap.Currency, now);
        return true;
    }

    /// <summary>Upsert the hq_credit_notes read-model from a CreditNoteIssued snapshot.</summary>
    private bool ProjectCreditNote(Guid tenantId, SyncChangeDto change, Dictionary<Guid, HqCreditNote> existing, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(change.Snapshot)) return false;
        CreditNoteSnapshot? snap;
        try { snap = JsonSerializer.Deserialize<CreditNoteSnapshot>(change.Snapshot!, Json); }
        catch (JsonException ex) { _log.LogWarning(ex, "hq.sync.project bad credit-note snapshot for change {Id}", change.Id); return false; }
        if (snap is null) return false;

        if (!existing.TryGetValue(snap.CreditNoteId, out var note))
        {
            note = HqCreditNote.Create(snap.CreditNoteId, now);
            _db.HqCreditNotes.Add(note);
            existing[snap.CreditNoteId] = note;
        }
        note.Apply(tenantId, snap.StoreId, snap.ReturnNumber, snap.OriginalSaleId, snap.OriginalReceiptNumber,
            snap.Reason, snap.IsVoid, snap.AuthorizedByName, snap.RefundMethod, snap.RefundStatus,
            snap.GrandTotal, snap.Currency, snap.LineCount, snap.CreatedAtUtc, now);
        return true;
    }

    /// <summary>Add one synced movement's delta to the (store, product) on-hand running sum. Idempotent
    /// because a change is projected only once (the sync_inbox dedup skips re-received changes).</summary>
    private bool ProjectStock(Guid tenantId, SyncChangeDto change, Dictionary<(Guid Store, Guid Product), HqStockOnHand> existing, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(change.Snapshot)) return false;
        StockMovementSnapshot? snap;
        try { snap = JsonSerializer.Deserialize<StockMovementSnapshot>(change.Snapshot!, Json); }
        catch (JsonException ex) { _log.LogWarning(ex, "hq.sync.project bad stock snapshot for change {Id}", change.Id); return false; }
        if (snap is null) return false;

        var key = (snap.StoreId, snap.ProductId);
        if (!existing.TryGetValue(key, out var row))
        {
            row = HqStockOnHand.Create(tenantId, snap.StoreId, snap.ProductId);
            _db.HqStockOnHand.Add(row);
            existing[key] = row;
        }
        row.AddDelta(snap.QuantityDelta, snap.Sku, snap.Name, snap.UnitOfMeasure, snap.OccurredAtUtc, now);
        return true;
    }

    /// <summary>Route a dispatched inter-branch transfer into hq_transfers (M3). Idempotent on the transfer id.</summary>
    private bool ProjectTransfer(Guid tenantId, SyncChangeDto change, Dictionary<Guid, HqTransfer> existing, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(change.Snapshot)) return false;
        TransferSnapshot? snap;
        try { snap = JsonSerializer.Deserialize<TransferSnapshot>(change.Snapshot!, Json); }
        catch (JsonException ex) { _log.LogWarning(ex, "hq.sync.project bad transfer snapshot for change {Id}", change.Id); return false; }
        if (snap is null) return false;
        if (existing.ContainsKey(snap.TransferId)) return true; // already routed

        var linesJson = JsonSerializer.Serialize(snap.Lines, Json);
        var transfer = HqTransfer.Create(snap.TransferId, tenantId, snap.FromStoreId, snap.ToStoreId, snap.ToStoreName,
            snap.DispatchedByName, snap.DispatchedAtUtc, snap.Note, snap.Lines.Count, linesJson, now);
        _db.HqTransfers.Add(transfer);
        existing[snap.TransferId] = transfer;
        return true;
    }

    private static List<Guid> ExtractStockProductIds(IReadOnlyList<SyncChangeDto> changes)
    {
        var ids = new HashSet<Guid>();
        foreach (var c in changes)
        {
            if (c.EventType != StockMovementType || string.IsNullOrWhiteSpace(c.Snapshot)) continue;
            try
            {
                var s = JsonSerializer.Deserialize<StockMovementSnapshot>(c.Snapshot!, Json);
                if (s is not null) ids.Add(s.ProductId);
            }
            catch (JsonException) { /* skip; ProjectStock logs it */ }
        }
        return ids.ToList();
    }
}
