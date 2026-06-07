using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Printing;
using Pos.Application.Receipts;
using Pos.Application.Sales;
using Pos.Domain.Tenancy;

namespace Pos.Api.Endpoints;

internal static class ReturnEndpoints
{
    public static IEndpointRouteBuilder MapReturns(this IEndpointRouteBuilder app)
    {
        // Create a return / void (Supervisor or Manager only). Over-returns → 409; bad input → 400.
        app.MapPost("/sales/{saleId:guid}/returns", async (
            Guid saleId, CreateReturnRequest req, ReturnService returns, ReceiptService receipts,
            ReceiptOutputService output, CancellationToken ct) =>
        {
            var note = await returns.ProcessAsync(saleId, req.ReturnId, req.Reason,
                req.Lines.Select(l => (l.ProductId, l.Quantity)).ToList(), req.RefundMethod, req.RegisterId, ct);
            if (note is null) return Results.NotFound(); // original sale not in this store

            await output.PrintReturnAsync(req.RegisterId, note.Id, ct); // print on the till's profile

            var receipt = (await receipts.GetReturnAsync(note.Id, null, ct))!;
            return Results.Created($"/api/v1/returns/{note.Id}", new ReturnResponse(
                note.Id, note.ReturnNumber!, note.RefundAmount.Amount, note.RefundStatus.ToString(),
                new ReceiptResponse(receipt.Model, receipt.Text, receipt.Html, receipt.Columns)));
        }).RequireAuthorization("SupervisorOrAbove").WithTags("Returns");

        app.MapGet("/returns/{returnId:guid}/receipt", async (
            Guid returnId, int? cols, ReceiptService receipts, CancellationToken ct) =>
        {
            var r = await receipts.GetReturnAsync(returnId, cols, ct);
            return r is null ? Results.NotFound() : Results.Ok(new ReceiptResponse(r.Model, r.Text, r.Html, r.Columns));
        }).WithTags("Returns");

        app.MapGet("/returns/{returnId:guid}/receipt/preview.png", async (
            Guid returnId, string? paper, ReceiptOutputService output, CancellationToken ct) =>
        {
            var png = await output.PreviewReturnAsync(returnId, paper == "58" ? PaperWidth.Mm58 : PaperWidth.Mm80, ct);
            return png is null ? Results.NotFound() : Results.File(png, "image/png");
        }).WithTags("Returns");

        return app;
    }
}
