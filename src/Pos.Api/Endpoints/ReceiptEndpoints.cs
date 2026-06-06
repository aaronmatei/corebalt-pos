using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Printing;
using Pos.Application.Receipts;
using Pos.Domain.Tenancy;

namespace Pos.Api.Endpoints;

internal static class ReceiptEndpoints
{
    public static IEndpointRouteBuilder MapReceipts(this IEndpointRouteBuilder app)
    {
        // The till calls this after a successful checkout. ?cols=32 for 58mm paper (default 48 = 80mm).
        app.MapGet("/sales/{saleId:guid}/receipt", async (
            Guid saleId,
            int? cols,
            ReceiptService receipts,
            CancellationToken ct) =>
        {
            var r = await receipts.GetAsync(saleId, cols, ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new ReceiptResponse(r.Model, r.Text, r.Html, r.Columns));
        }).WithTags("Sales");

        // Printer-free visual preview: a PNG that mirrors the paper (?paper=58 for 58mm, default 80mm).
        app.MapGet("/sales/{saleId:guid}/receipt/preview.png", async (
            Guid saleId, string? paper, ReceiptOutputService output, CancellationToken ct) =>
        {
            var png = await output.PreviewSaleAsync(saleId, paper == "58" ? PaperWidth.Mm58 : PaperWidth.Mm80, ct);
            return png is null ? Results.NotFound() : Results.File(png, "image/png");
        }).WithTags("Sales");

        return app;
    }
}
