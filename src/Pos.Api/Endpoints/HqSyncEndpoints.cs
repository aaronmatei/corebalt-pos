using Pos.Application.Sync;
using Pos.Application.Tenancy;

namespace Pos.Api.Endpoints;

/// <summary>
/// HQ/cloud ingest for store→cloud sync. An on-prem store server POSTs a batch of its outbox changes
/// here; the cloud durably stores them (idempotent) and projects read-models. Authenticated by the
/// tenant's SYNC TOKEN (header <c>X-Sync-Token</c>) rather than a user — the store has no cloud user.
/// Mapped only in Hq mode; lives on the apex (no tenant subdomain needed — the body names the tenant).
/// </summary>
internal static class HqSyncEndpoints
{
    public static IEndpointRouteBuilder MapHqSync(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/hq/sync").AllowAnonymous();

        g.MapPost("/ingest", async (HttpContext http, SyncIngestRequest body,
            ITenantRepository tenants, IHqSyncIngestService ingest, CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.TenantSlug)) return Results.BadRequest();

            var tenant = await tenants.GetBySlugAsync(body.TenantSlug.Trim().ToLowerInvariant(), ct);
            if (tenant is null || !tenant.IsActive) return Results.Unauthorized();

            var presented = http.Request.Headers.TryGetValue(SyncHeaders.Token, out var v) && v.Count == 1 ? v[0] : null;
            if (!SyncToken.Verify(presented, tenant.SyncSecretHash)) return Results.Unauthorized();

            var result = await ingest.IngestAsync(tenant.Id, body, ct);
            return Results.Ok(result);
        });

        return app;
    }
}
