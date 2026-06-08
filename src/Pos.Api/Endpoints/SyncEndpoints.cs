using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Application.Abstractions;
using Pos.Application.Sync;

namespace Pos.Api.Endpoints;

/// <summary>
/// HQ-sync seam (roadmap M1): lets the future HQ/cloud tier PULL the store's transactional-outbox changes
/// and acknowledge them. Read + ack only — the store stays authoritative; HQ never writes back here. The
/// outbox is already written in the checkout transaction, so this just exposes it. Manager-gated for now
/// (a dedicated HQ/sync credential is the future hardening). At-least-once: ack after you've durably
/// stored the batch; re-reads are safe because HQ keys on the change id.
/// </summary>
internal static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSync(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/sync").WithTags("Sync").RequireAuthorization("Manager");

        // GET /sync/changes?max=200 — the next batch of not-yet-shipped changes, time-ordered.
        g.MapGet("/changes", async (int? max, ICurrentContext ctx, IOutboxSyncStore store, CancellationToken ct) =>
        {
            var take = max is > 0 ? max.Value : 200;
            var changes = await store.ReadUnprocessedAsync(ctx.TenantId, ctx.StoreId, take, ct);
            var remaining = await store.CountUnprocessedAsync(ctx.TenantId, ctx.StoreId, ct);
            return Results.Ok(new ChangeFeedResponse(changes, remaining, HasMore: remaining > changes.Count));
        });

        // POST /sync/ack { ids: [...] } — mark a processed batch as shipped.
        g.MapPost("/ack", async (AckRequest req, ICurrentContext ctx, IOutboxSyncStore store, CancellationToken ct) =>
        {
            var acknowledged = await store.AcknowledgeAsync(ctx.TenantId, ctx.StoreId, req.Ids ?? [], ct);
            return Results.Ok(new AckResponse(acknowledged));
        });

        return app;
    }
}

public sealed record ChangeFeedResponse(IReadOnlyList<OutboxChange> Changes, int Remaining, bool HasMore);
public sealed record AckRequest(IReadOnlyList<Guid>? Ids);
public sealed record AckResponse(int Acknowledged);
