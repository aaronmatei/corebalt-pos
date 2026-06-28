using System.Text.Json;
using Pos.Application.Cash;
using Pos.Application.Catalog;
using Pos.Application.Identity;
using Pos.Application.Sales;
using Pos.Domain.Cash.Events;
using Pos.Domain.Inventory.Events;
using Pos.Domain.Sales.Events;

namespace Pos.Application.Sync;

/// <summary>
/// On-prem store→cloud push. One pass: read this store's unprocessed outbox (via the existing
/// <see cref="IOutboxSyncStore"/> seam), hydrate the event types the cloud projects (SaleCompleted →
/// full <see cref="SaleSnapshot"/>), POST the batch, and ACK exactly the ids the cloud accepted —
/// which stamps <c>ProcessedAtUtc</c> (the HQ-shipped marker). At-least-once and NAT-friendly: the
/// store always initiates; a failed push acks nothing and is retried next pass.
/// <para>NOTE: ProcessedAtUtc is the single "shipped to HQ" marker; do not also run the optional
/// Corebalt ERP forwarder against the same outbox (it would mark rows this never sees).</para>
/// </summary>
public sealed class HqSyncPusher
{
    private static readonly string SaleCompletedType = typeof(SaleCompleted).FullName!;
    private static readonly string SessionClosedType = typeof(RegisterSessionClosed).FullName!;
    private static readonly string CreditNoteIssuedType = typeof(CreditNoteIssued).FullName!;
    private static readonly string StockMovementType = typeof(StockMovementRecorded).FullName!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IOutboxSyncStore _outbox;
    private readonly ISaleRepository _sales;
    private readonly IRegisterSessionRepository _sessions;
    private readonly ICreditNoteRepository _creditNotes;
    private readonly IProductRepository _products;
    private readonly IHqSyncClient _client;
    private readonly StoreServerOptions _server;
    private readonly HqSyncOptions _options;

    public HqSyncPusher(IOutboxSyncStore outbox, ISaleRepository sales, IRegisterSessionRepository sessions,
        ICreditNoteRepository creditNotes, IProductRepository products,
        IHqSyncClient client, StoreServerOptions server, HqSyncOptions options)
    {
        _outbox = outbox;
        _sales = sales;
        _sessions = sessions;
        _creditNotes = creditNotes;
        _products = products;
        _client = client;
        _server = server;
        _options = options;
    }

    /// <summary>Returns the number of changes acked (shipped) this pass.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled) return 0;
        var tenant = _server.TenantId;
        var store = _server.StoreId;
        if (tenant == Guid.Empty || store == Guid.Empty) return 0; // not configured

        var changes = await _outbox.ReadUnprocessedAsync(tenant, store, _options.BatchSize, ct);
        if (changes.Count == 0) return 0;

        var dtos = new List<SyncChangeDto>(changes.Count);
        foreach (var c in changes)
        {
            string? snapshot = null;
            if (c.EventType == SaleCompletedType)
            {
                var sale = await _sales.GetAsync(tenant, store, c.AggregateId, ct);
                if (sale is not null) snapshot = JsonSerializer.Serialize(SaleSnapshotFactory.From(sale), Json);
            }
            else if (c.EventType == SessionClosedType)
            {
                var session = await _sessions.GetAsync(tenant, store, c.AggregateId, ct);
                if (session is not null) snapshot = JsonSerializer.Serialize(SessionSnapshotFactory.From(session), Json);
            }
            else if (c.EventType == CreditNoteIssuedType)
            {
                var note = await _creditNotes.GetAsync(tenant, store, c.AggregateId, ct);
                if (note is not null) snapshot = JsonSerializer.Serialize(CreditNoteSnapshotFactory.From(note), Json);
            }
            else if (c.EventType == StockMovementType)
            {
                // The fact is fully in the event; just enrich with the product's Sku/Name for the cloud view.
                var evt = JsonSerializer.Deserialize<StockMovementRecorded>(c.Payload, Json);
                if (evt is not null)
                {
                    var product = await _products.GetAsync(tenant, store, evt.ProductId, ct);
                    snapshot = JsonSerializer.Serialize(StockMovementSnapshotFactory.From(evt, product, c.OccurredAtUtc), Json);
                }
            }
            dtos.Add(new SyncChangeDto(c.Id, c.AggregateId, c.EventType, c.OccurredAtUtc, c.EnqueuedAtUtc, c.Payload, snapshot));
        }

        var response = await _client.PushAsync(new SyncIngestRequest(_options.TenantSlug, store, dtos), ct);

        // Ack only what the cloud durably accepted; anything else stays unprocessed for the next pass.
        return await _outbox.AcknowledgeAsync(tenant, store, response.AcceptedIds ?? [], ct);
    }
}
