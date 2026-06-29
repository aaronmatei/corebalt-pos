using Pos.Application.Sync;
using Pos.Application.Tenancy;
using Pos.Domain.Tenancy;

namespace Pos.Api.Endpoints;

/// <summary>
/// HQ-side inter-branch transfer routing (M3), sync-token authed per store:
/// <list type="bullet">
/// <item><c>GET /hq/transfers/incoming?slug=&amp;storeId=</c> — undelivered transfers TO this store.</item>
/// <item><c>POST /hq/transfers/{id}/received?slug=&amp;storeId=</c> — the destination acks receipt.</item>
/// <item><c>GET /hq/branches?slug=&amp;storeId=</c> — the tenant's other branches (destination picker).</item>
/// </list>
/// </summary>
internal static class HqTransferEndpoints
{
    public static IEndpointRouteBuilder MapHqTransfers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/hq/transfers/incoming", async (HttpContext http, string? slug, Guid? storeId,
            ITenantRepository tenants, IHqTransferStore store, CancellationToken ct) =>
        {
            var tenant = await Authed(http, slug, tenants, ct);
            if (tenant is null) return Results.Unauthorized();
            if (storeId is null || storeId == Guid.Empty) return Results.BadRequest();
            return Results.Ok(new IncomingTransfersResponse(await store.IncomingAsync(tenant.Id, storeId.Value, ct)));
        }).AllowAnonymous();

        app.MapPost("/hq/transfers/{id:guid}/received", async (Guid id, HttpContext http, string? slug, Guid? storeId,
            ITenantRepository tenants, IHqTransferStore store, CancellationToken ct) =>
        {
            var tenant = await Authed(http, slug, tenants, ct);
            if (tenant is null) return Results.Unauthorized();
            if (storeId is null || storeId == Guid.Empty) return Results.BadRequest();
            return await store.MarkReceivedAsync(tenant.Id, storeId.Value, id, ct) ? Results.NoContent() : Results.NotFound();
        }).AllowAnonymous();

        app.MapGet("/hq/branches", async (HttpContext http, string? slug, Guid? storeId,
            ITenantRepository tenants, IHqTransferStore store, CancellationToken ct) =>
        {
            var tenant = await Authed(http, slug, tenants, ct);
            if (tenant is null) return Results.Unauthorized();
            return Results.Ok(new BranchesResponse(await store.BranchesAsync(tenant.Id, storeId ?? Guid.Empty, ct)));
        }).AllowAnonymous();

        return app;
    }

    private static async Task<Tenant?> Authed(HttpContext http, string? slug, ITenantRepository tenants, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var tenant = await tenants.GetBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
        if (tenant is null || !tenant.IsActive) return null;
        var presented = http.Request.Headers.TryGetValue(SyncHeaders.Token, out var v) && v.Count == 1 ? v[0] : null;
        return SyncToken.Verify(presented, tenant.SyncSecretHash) ? tenant : null;
    }
}
