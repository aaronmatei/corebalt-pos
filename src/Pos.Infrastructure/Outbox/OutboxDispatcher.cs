using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Abstractions;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Outbox;

/// <summary>
/// Step-2 dispatcher: marks unprocessed outbox rows as shipped and logs them. The real
/// HQ transport (queue / HTTP) lands in step 3; the contract for callers — "DrainAsync moves
/// pending rows forward at least once" — is what we're locking in here.
/// </summary>
internal sealed class OutboxDispatcher : IOutboxDispatcher
{
    private readonly PosDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<OutboxDispatcher> _log;

    public OutboxDispatcher(PosDbContext db, IClock clock, ILogger<OutboxDispatcher> log)
    { _db = db; _clock = clock; _log = log; }

    public async Task<int> DrainAsync(int batchSize = 100, CancellationToken ct = default)
    {
        var pending = await _db.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null)
            .OrderBy(m => m.OccurredAtUtc).ThenBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var now = _clock.UtcNow;
        foreach (var msg in pending)
        {
            _log.LogInformation("outbox.shipped tenant={Tenant} store={Store} agg={Agg} type={Type}",
                msg.TenantId, msg.StoreId, msg.AggregateId, msg.EventType);
            msg.MarkProcessed(now);
        }
        await _db.SaveChangesAsync(ct);
        return pending.Count;
    }
}
