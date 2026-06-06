using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Infrastructure.Outbox;

/// <summary>
/// The transactional outbox row. Written in the SAME database transaction as the aggregate
/// change that produced the event, so we never have a "saved but never published" gap when
/// the store server crashes mid-checkout. The dispatcher later ships these to HQ at least once.
/// </summary>
public sealed class OutboxMessage : ITenantScoped, IStoreScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid AggregateId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;     // jsonb
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset EnqueuedAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage() { } // EF

    public static OutboxMessage Enqueue(Guid tenantId, Guid storeId, Guid aggregateId,
        string eventType, string payload, DateTimeOffset occurredAtUtc, DateTimeOffset now) => new()
    {
        Id = Uuid7.NewGuid(),
        TenantId = tenantId,
        StoreId = storeId,
        AggregateId = aggregateId,
        EventType = eventType,
        Payload = payload,
        OccurredAtUtc = occurredAtUtc,
        EnqueuedAtUtc = now,
        Attempts = 0
    };

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAtUtc = now;
        LastError = null;
    }

    public void MarkFailed(DateTimeOffset now, string error)
    {
        Attempts += 1;
        LastError = error;
        // Leave ProcessedAtUtc null so the dispatcher retries.
    }
}
