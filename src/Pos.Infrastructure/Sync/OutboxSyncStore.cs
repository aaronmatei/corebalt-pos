using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Sync;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Sync;

/// <summary>
/// Reads/acks transactional-outbox rows for the HQ-sync pull endpoint. Ordering matches the dispatcher
/// (OccurredAtUtc then Id) so a batch is a stable, time-ordered slice. Ack stamps ProcessedAtUtc inside a
/// SaveChanges — the same field <see cref="Pos.Infrastructure.Outbox.OutboxMessage.MarkProcessed"/> sets.
/// </summary>
internal sealed class OutboxSyncStore : IOutboxSyncStore
{
    private readonly PosDbContext _db;
    private readonly IClock _clock;

    public OutboxSyncStore(PosDbContext db, IClock clock) { _db = db; _clock = clock; }

    public async Task<IReadOnlyList<OutboxChange>> ReadUnprocessedAsync(Guid tenantId, Guid storeId, int max, CancellationToken ct = default) =>
        await _db.OutboxMessages
            .Where(m => m.TenantId == tenantId && m.StoreId == storeId && m.ProcessedAtUtc == null)
            .OrderBy(m => m.OccurredAtUtc).ThenBy(m => m.Id)
            .Take(Math.Clamp(max, 1, 1000))
            .Select(m => new OutboxChange(
                m.Id, m.TenantId, m.StoreId, m.AggregateId, m.EventType, m.Payload, m.OccurredAtUtc, m.EnqueuedAtUtc))
            .ToListAsync(ct);

    public Task<int> CountUnprocessedAsync(Guid tenantId, Guid storeId, CancellationToken ct = default) =>
        _db.OutboxMessages.CountAsync(m => m.TenantId == tenantId && m.StoreId == storeId && m.ProcessedAtUtc == null, ct);

    public async Task<int> AcknowledgeAsync(Guid tenantId, Guid storeId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        if (ids is null || ids.Count == 0) return 0;
        var rows = await _db.OutboxMessages
            .Where(m => m.TenantId == tenantId && m.StoreId == storeId && m.ProcessedAtUtc == null && ids.Contains(m.Id))
            .ToListAsync(ct);

        var now = _clock.UtcNow;
        foreach (var row in rows) row.MarkProcessed(now);
        await _db.SaveChangesAsync(ct);
        return rows.Count;
    }
}
