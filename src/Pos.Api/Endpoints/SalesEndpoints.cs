using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Abstractions;
using Pos.Application.Fiscalization;
using Pos.Application.Sales;

namespace Pos.Api.Endpoints;

internal static class SalesEndpoints
{
    public static IEndpointRouteBuilder MapSales(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/sales").WithTags("Sales");

        // One-shot atomic checkout — the till's primary path. Builds, tenders, and completes
        // the whole sale in a single transaction; returns 201 with the sale id + change due.
        g.MapPost("/checkout", async (
            CheckoutRequest req,
            CheckoutService checkout,
            FiscalizationService fiscal,
            Pos.Application.Printing.ReceiptOutputService output,
            ICurrentContext ctx,
            CancellationToken ct) =>
        {
            var result = await checkout.CheckoutAsync(
                req.RegisterId,
                req.Currency,
                req.Lines.Select(l => new CheckoutLine(l.ProductId, l.Quantity)).ToList(),
                req.Tenders.Select(t => new CheckoutTender(t.Type, t.Amount, t.Reference)).ToList(),
                ct);
            // Fiscalize right after the sale is committed, so the receipt fetched next has the fiscal block.
            await fiscal.FiscalizeAsync(ctx.TenantId, ctx.StoreId, result.SaleId, ct);
            // Build ESC/POS for the register's printer + send it (NullPrinter by default; never fails the sale).
            await output.PrintSaleAsync(req.RegisterId, result.SaleId, ct);
            return Results.Created($"/api/v1/sales/{result.SaleId}",
                new CompleteSaleResponse(result.SaleId, result.Total, result.ChangeDue, result.Currency));
        });

        g.MapPost("/", async (
            StartSaleRequest req,
            CheckoutService checkout,
            CancellationToken ct) =>
        {
            var saleId = await checkout.StartAsync(req.RegisterId, req.Currency, ct);
            return Results.Created($"/api/v1/sales/{saleId}", new StartSaleResponse(saleId));
        });

        g.MapPost("/{saleId:guid}/lines", async (
            Guid saleId,
            AddLineRequest req,
            CheckoutService checkout,
            CancellationToken ct) =>
        {
            await checkout.AddLineAsync(saleId, req.ProductId, req.Quantity, ct);
            return Results.NoContent();
        });

        g.MapPost("/{saleId:guid}/tenders", async (
            Guid saleId,
            AddTenderRequest req,
            CheckoutService checkout,
            CancellationToken ct) =>
        {
            await checkout.AddTenderAsync(saleId, req.Type, req.Amount, req.Reference, ct);
            return Results.NoContent();
        });

        g.MapPost("/{saleId:guid}/complete", async (
            Guid saleId,
            CheckoutService checkout,
            FiscalizationService fiscal,
            ICurrentContext ctx,
            CancellationToken ct) =>
        {
            var result = await checkout.CompleteAsync(saleId, ct);
            await fiscal.FiscalizeAsync(ctx.TenantId, ctx.StoreId, result.SaleId, ct);
            return Results.Ok(new CompleteSaleResponse(
                result.SaleId, result.Total, result.ChangeDue, result.Currency));
        });

        g.MapGet("/{saleId:guid}", async (
            Guid saleId,
            ICurrentContext ctx,
            ISaleRepository sales,
            CancellationToken ct) =>
        {
            var sale = await sales.GetAsync(ctx.TenantId, ctx.StoreId, saleId, ct);
            return sale is null ? Results.NotFound() : Results.Ok(sale.ToResponse());
        });

        return app;
    }
}
