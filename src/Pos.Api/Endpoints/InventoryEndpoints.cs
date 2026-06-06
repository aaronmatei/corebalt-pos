using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Inventory;
using Pos.Domain.Inventory;

namespace Pos.Api.Endpoints;

internal static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventory(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/inventory").WithTags("Inventory");

        // On-hand for one product — always the SUM of immutable movements, never a stored column.
        g.MapGet("/{productId:guid}/on-hand", async (
            Guid productId,
            ICurrentContext ctx,
            IStockMovementRepository stock,
            CancellationToken ct) =>
        {
            var onHand = await stock.GetOnHandAsync(ctx.TenantId, ctx.StoreId, productId, ct);
            return Results.Ok(new StockOnHandResponse(productId, onHand));
        });

        // Stock report for the store: every product with its derived on-hand.
        g.MapGet("/report", async (
            ICurrentContext ctx,
            IProductRepository products,
            IStockMovementRepository stock,
            CancellationToken ct) =>
        {
            var all = await products.ListAsync(ctx.TenantId, ctx.StoreId, includeInactive: true, ct);
            var onHand = await stock.GetOnHandByProductAsync(ctx.TenantId, ctx.StoreId, ct);
            var rows = all.Select(p => new StockReportRow(
                p.Id, p.Sku, p.Name, p.UnitOfMeasure, p.IsActive,
                onHand.TryGetValue(p.Id, out var v) ? v : 0m)).ToList();
            return Results.Ok(new StockReportResponse(rows));
        });

        // Receive stock IN: a positive quantity, one immutable movement (Purchase/OpeningBalance/Adjustment).
        g.MapPost("/receive", async (
            ReceiveStockRequest req,
            ICurrentContext ctx,
            IProductRepository products,
            IStockMovementRepository stock,
            IUnitOfWork uow,
            CancellationToken ct) =>
        {
            if (req.Quantity <= 0)
                return Results.Problem("Receive quantity must be positive.", statusCode: StatusCodes.Status400BadRequest);
            if (req.Reason is not (StockMovementReason.Purchase or StockMovementReason.OpeningBalance or StockMovementReason.Adjustment))
                return Results.Problem("Receive reason must be Purchase, OpeningBalance or Adjustment.", statusCode: StatusCodes.Status400BadRequest);
            if (await products.GetAsync(ctx.TenantId, ctx.StoreId, req.ProductId, ct) is null)
                return Results.NotFound();

            var movement = StockMovement.Record(ctx.TenantId, ctx.StoreId, req.ProductId, req.Quantity, req.Reason, sourceRef: null, reference: req.Reference);
            await stock.AddAsync(movement, ct);
            await uow.SaveChangesAsync(ct);

            var onHand = await stock.GetOnHandAsync(ctx.TenantId, ctx.StoreId, req.ProductId, ct);
            return Results.Created($"/api/v1/inventory/{req.ProductId}/on-hand",
                new StockMovementResponse(movement.Id, req.ProductId, movement.QuantityDelta, movement.Reason, movement.Reference, onHand));
        });

        // Adjust stock: a SIGNED quantity (stock take / shrinkage), one immutable movement — never an edit.
        g.MapPost("/adjust", async (
            AdjustStockRequest req,
            ICurrentContext ctx,
            IProductRepository products,
            IStockMovementRepository stock,
            IUnitOfWork uow,
            CancellationToken ct) =>
        {
            if (req.Quantity == 0)
                return Results.Problem("Adjustment quantity cannot be zero.", statusCode: StatusCodes.Status400BadRequest);
            if (await products.GetAsync(ctx.TenantId, ctx.StoreId, req.ProductId, ct) is null)
                return Results.NotFound();

            var movement = StockMovement.Record(ctx.TenantId, ctx.StoreId, req.ProductId, req.Quantity, StockMovementReason.Adjustment, sourceRef: null, reference: req.Reference);
            await stock.AddAsync(movement, ct);
            await uow.SaveChangesAsync(ct);

            var onHand = await stock.GetOnHandAsync(ctx.TenantId, ctx.StoreId, req.ProductId, ct);
            return Results.Created($"/api/v1/inventory/{req.ProductId}/on-hand",
                new StockMovementResponse(movement.Id, req.ProductId, movement.QuantityDelta, movement.Reason, movement.Reference, onHand));
        });

        return app;
    }
}
