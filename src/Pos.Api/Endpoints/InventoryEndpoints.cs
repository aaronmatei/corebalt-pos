using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Inventory;

namespace Pos.Api.Endpoints;

internal static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventory(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/inventory").WithTags("Inventory");                          // reads: any authenticated user
        var mgr = app.MapGroup("/inventory").WithTags("Inventory").RequireAuthorization("Manager"); // stock ops + report: Manager

        // All stock logic lives in StockService (shared with the back-office). On-hand is always SUM(movements).
        g.MapGet("/{productId:guid}/on-hand", async (Guid productId, StockService svc, CancellationToken ct) =>
            Results.Ok(new StockOnHandResponse(productId, await svc.GetOnHandAsync(productId, ct))));

        mgr.MapGet("/report", async (StockService svc, CancellationToken ct) =>
        {
            var rows = (await svc.GetReportAsync(ct))
                .Select(r => new StockReportRow(r.ProductId, r.Sku, r.Name, r.Unit, r.IsActive, r.OnHand)).ToList();
            return Results.Ok(new StockReportResponse(rows));
        });

        mgr.MapPost("/receive", async (ReceiveStockRequest req, StockService svc, CancellationToken ct) =>
        {
            var result = await svc.ReceiveAsync(req.ProductId, req.Quantity, req.Reason, req.Reference, ct);
            return result is null
                ? Results.NotFound()
                : Results.Created($"/api/v1/inventory/{req.ProductId}/on-hand",
                    new StockMovementResponse(result.Value.Movement.Id, req.ProductId,
                        result.Value.Movement.QuantityDelta, result.Value.Movement.Reason,
                        result.Value.Movement.Reference, result.Value.OnHand));
        });

        mgr.MapPost("/adjust", async (AdjustStockRequest req, StockService svc, CancellationToken ct) =>
        {
            var result = await svc.AdjustAsync(req.ProductId, req.Quantity, req.Reference, ct);
            return result is null
                ? Results.NotFound()
                : Results.Created($"/api/v1/inventory/{req.ProductId}/on-hand",
                    new StockMovementResponse(result.Value.Movement.Id, req.ProductId,
                        result.Value.Movement.QuantityDelta, result.Value.Movement.Reason,
                        result.Value.Movement.Reference, result.Value.OnHand));
        });

        return app;
    }
}
