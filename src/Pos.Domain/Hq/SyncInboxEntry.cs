using Pos.SharedKernel;

namespace Pos.Domain.Hq;

/// <summary>
/// HQ/cloud side: a durable record of one outbox change received from an on-prem store (the seam that
/// makes ingestion at-least-once + idempotent). <see cref="Entity.Id"/> is the ORIGINAL store-side outbox
/// id, so re-receiving the same change is a no-op. Read-models are projected FROM these rows; the raw
/// payload/snapshot is kept so a new projection can be back-filled later without re-syncing.
/// </summary>
public sealed class SyncInboxEntry : Entity, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid AggregateId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public string? Snapshot { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset EnqueuedAtUtc { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset? ProjectedAtUtc { get; private set; }

    private SyncInboxEntry() { } // EF

    public static SyncInboxEntry Receive(Guid id, Guid tenantId, Guid storeId, Guid aggregateId,
        string eventType, string payload, string? snapshot,
        DateTimeOffset occurredAtUtc, DateTimeOffset enqueuedAtUtc, DateTimeOffset now) => new()
    {
        Id = id,
        TenantId = tenantId,
        StoreId = storeId,
        AggregateId = aggregateId,
        EventType = eventType,
        Payload = payload,
        Snapshot = snapshot,
        OccurredAtUtc = occurredAtUtc,
        EnqueuedAtUtc = enqueuedAtUtc,
        ReceivedAtUtc = now,
    };

    public void MarkProjected(DateTimeOffset now) => ProjectedAtUtc = now;
}
