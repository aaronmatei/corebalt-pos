using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Abstractions;
using Pos.Application.Inventory;

namespace Pos.Api.Endpoints;

internal static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventory(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/inventory").WithTags("Inventory");

        g.MapGet("/{productId:guid}/on-hand", async (
            Guid productId,
            ICurrentContext ctx,
            IStockMovementRepository stock,
            CancellationToken ct) =>
        {
            // Always returns 200 with the SUM — zero is a valid answer ("we have none").
            // The SUM is computed by Postgres; we never materialize a mutable on-hand column.
            var onHand = await stock.GetOnHandAsync(ctx.TenantId, ctx.StoreId, productId, ct);
            return Results.Ok(new StockOnHandResponse(productId, onHand));
        });

        return app;
    }
}
