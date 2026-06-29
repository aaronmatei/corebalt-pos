using Pos.Api.Contracts;
using Pos.Application.Catalog;
using Pos.Application.Sync;
using Pos.Application.Tenancy;
using Pos.Domain.Catalog;
using Pos.SharedKernel;

namespace Pos.Api.Endpoints;

/// <summary>
/// HQ catalogue master (M2): manage the tenant's central catalogue (Manager-gated JSON API), and the
/// store-pull feed (<c>GET /hq/catalog/changes</c>, sync-token auth) branches drain to apply by SKU.
/// </summary>
internal static class CatalogMasterEndpoints
{
    public static IEndpointRouteBuilder MapCatalogMaster(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/catalog").RequireAuthorization("Manager");

        g.MapGet("/", async (CatalogItemService svc, bool? includeInactive, CancellationToken ct) =>
            Results.Ok((await svc.ListAsync(includeInactive ?? false, ct)).Select(ToResponse)));

        g.MapGet("/{id:guid}", async (Guid id, CatalogItemService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } i ? Results.Ok(ToResponse(i)) : Results.NotFound());

        g.MapPost("/", async (CreateCatalogItemRequest req, CatalogItemService svc, CancellationToken ct) =>
        {
            var item = await svc.CreateAsync(req.Sku, req.Name, new Money(req.PriceAmount, req.PriceCurrency),
                req.TaxClass, req.UnitOfMeasure, req.Barcode, req.CategoryName, ct);
            return Results.Created($"/api/v1/catalog/{item.Id}", ToResponse(item));
        });

        g.MapPut("/{id:guid}", async (Guid id, UpdateCatalogItemRequest req, CatalogItemService svc, CancellationToken ct) =>
            await svc.UpdateAsync(id, req.Name, req.Barcode, req.UnitOfMeasure, req.TaxClass, req.CategoryName, ct) is { } i
                ? Results.Ok(ToResponse(i)) : Results.NotFound());

        g.MapPut("/{id:guid}/price", async (Guid id, RepriceProductRequest req, CatalogItemService svc, CancellationToken ct) =>
            await svc.RepriceAsync(id, new Money(req.Amount, req.Currency), ct) is { } i
                ? Results.Ok(ToResponse(i)) : Results.NotFound());

        g.MapPost("/{id:guid}/active", async (Guid id, SetCatalogActiveRequest req, CatalogItemService svc, CancellationToken ct) =>
            await svc.SetActiveAsync(id, req.Active, ct) is { } i ? Results.Ok(ToResponse(i)) : Results.NotFound());

        return app;
    }

    /// <summary>Store-pull feed. Authenticated by the tenant's sync token (same as the ingest), not a user.</summary>
    public static IEndpointRouteBuilder MapCatalogPull(this IEndpointRouteBuilder app)
    {
        app.MapGet("/hq/catalog/changes", async (HttpContext http, string? slug, long? since, int? max,
            ITenantRepository tenants, ICatalogChangeRepository changes, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(slug)) return Results.BadRequest();
            var tenant = await tenants.GetBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null || !tenant.IsActive) return Results.Unauthorized();
            var presented = http.Request.Headers.TryGetValue(SyncHeaders.Token, out var v) && v.Count == 1 ? v[0] : null;
            if (!SyncToken.Verify(presented, tenant.SyncSecretHash)) return Results.Unauthorized();

            var take = max is > 0 ? Math.Min(max.Value, 500) : 200;
            var rows = await changes.ListSinceAsync(tenant.Id, since ?? 0, take, ct);
            var items = rows.Select(c => new CatalogItemDto(c.Seq, c.CatalogItemId, c.Sku, c.Name, c.PriceAmount,
                c.Currency, c.TaxClass, c.UnitOfMeasure, c.Barcode, c.IsActive, c.CategoryName)).ToList();
            var cursor = items.Count > 0 ? items[^1].Seq : (since ?? 0);
            return Results.Ok(new CatalogPullResponse(items, cursor, items.Count == take));
        }).AllowAnonymous();

        return app;
    }

    private static CatalogItemResponse ToResponse(CatalogItem i) => new(
        i.Id, i.Sku, i.Name, new MoneyDto(i.Price.Amount, i.Price.Currency), i.UnitOfMeasure, i.TaxClass,
        i.Barcode, i.IsActive, i.UpdatedAtUtc, i.CategoryName);
}
