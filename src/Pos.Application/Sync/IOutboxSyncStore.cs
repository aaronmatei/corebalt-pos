namespace Pos.Application.Sync;

/// <summary>
/// One change to ship to HQ — a flattened transactional-outbox row. The payload is the serialized domain
/// event; HQ deserializes by <see cref="EventType"/>. Edge-generated, time-ordered (<see cref="Id"/> is a
/// UUIDv7), store-authoritative (carries TenantId/StoreId) — exactly what M1 sync needs.
/// </summary>
public sealed record OutboxChange(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    Guid AggregateId,
    string EventType,
    string Payload,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset EnqueuedAtUtc);

/// <summary>
/// The HQ-sync read model over the transactional outbox. A puller (the future HQ/cloud tier — M1) reads a
/// batch of not-yet-shipped changes, processes them, then acknowledges them. Acknowledgement stamps
/// <c>ProcessedAtUtc</c>, which is the HQ-sync marker (kept separate from the in-app low-stock dispatcher,
/// which dedups on its own source id). At-least-once: a puller that crashes before ack just re-reads the
/// batch, so HQ must be idempotent on the change id.
/// </summary>
public interface IOutboxSyncStore
{
    Task<IReadOnlyList<OutboxChange>> ReadUnprocessedAsync(Guid tenantId, Guid storeId, int max, CancellationToken ct = default);
    Task<int> CountUnprocessedAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);
    /// <summary>Mark the given changes shipped. Tenant/store-guarded and idempotent (already-acked ids are skipped).</summary>
    Task<int> AcknowledgeAsync(Guid tenantId, Guid storeId, IReadOnlyList<Guid> ids, CancellationToken ct = default);
}
