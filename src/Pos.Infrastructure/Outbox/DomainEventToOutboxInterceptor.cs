using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pos.Application.Abstractions;
using Pos.SharedKernel;

namespace Pos.Infrastructure.Outbox;

/// <summary>
/// Drains every AggregateRoot.DomainEvents collection in the change tracker into outbox rows
/// just before the database transaction commits. Because the rows are inserted on the same
/// DbContext as the aggregate change, EF flushes them in one transaction — there is no
/// "saved but never published" window for the HQ sync to lose.
/// </summary>
internal sealed class DomainEventToOutboxInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IClock _clock;

    public DomainEventToOutboxInterceptor(IClock clock) => _clock = clock;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var ctx = eventData.Context;
        if (ctx is null) return base.SavingChangesAsync(eventData, result, ct);

        var aggregates = ctx.ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        var now = _clock.UtcNow;
        foreach (var aggregate in aggregates)
        {
            // Aggregate must be tenant- and store-scoped for the outbox row to inherit ownership.
            // (Every aggregate in this codebase implements both — guarded by the type check below.)
            if (aggregate is not ITenantScoped tenant || aggregate is not IStoreScoped store)
                throw new InvalidOperationException(
                    $"Aggregate {aggregate.GetType().Name} raised a domain event but is not tenant+store-scoped; " +
                    "the outbox needs both ids to route the event to HQ safely.");

            foreach (var evt in aggregate.DomainEvents)
            {
                var payload = JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions);
                var outbox = OutboxMessage.Enqueue(
                    tenantId: tenant.TenantId,
                    storeId: store.StoreId,
                    aggregateId: aggregate.Id,
                    eventType: evt.GetType().FullName ?? evt.GetType().Name,
                    payload: payload,
                    occurredAtUtc: evt.OccurredAtUtc,
                    now: now);
                ctx.Add(outbox);
            }
            aggregate.ClearDomainEvents();
        }

        return base.SavingChangesAsync(eventData, result, ct);
    }
}
