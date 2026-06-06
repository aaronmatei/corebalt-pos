using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Receipts;

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

        return app;
    }
}
