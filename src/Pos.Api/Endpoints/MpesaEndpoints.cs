using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Payments;
using Pos.Application.Sales;

namespace Pos.Api.Endpoints;

internal static class MpesaEndpoints
{
    /// <summary>Authenticated M-Pesa routes (under /api/v1): initiate an STK push and poll its status.</summary>
    public static IEndpointRouteBuilder MapMpesa(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/sales/mpesa").WithTags("M-Pesa");

        // Initiate: returns 202 Accepted while the customer is prompted (sale stays pending),
        // or 502 if Daraja rejected the STK push outright.
        g.MapPost("/checkout", async (
            MpesaCheckoutRequest req,
            MpesaPaymentService mpesa,
            CancellationToken ct) =>
        {
            var result = await mpesa.InitiateAsync(
                req.RegisterId,
                req.Currency,
                req.Lines.Select(l => new CheckoutLine(l.ProductId, l.Quantity)).ToList(),
                (req.CashTenders ?? Array.Empty<CheckoutTenderRequest>())
                    .Select(t => new CheckoutTender(t.Type, t.Amount, t.Reference)).ToList(),
                req.MpesaAmount,
                req.PhoneNumber,
                req.AccountReference ?? "POS",
                ct);

            var body = new MpesaInitiateResponse(
                result.SaleId, result.TenderId, result.CheckoutRequestId, result.Status, result.Message);

            // Both outcomes are "we processed the initiate" — the body's Status says what happened, so
            // the till branches on it rather than on the HTTP code. A rejected push is a business
            // result (200, Status=Failed, pending tender marked failed), not a transport error;
            // genuine errors (validation 400, domain 409) still surface as ProblemDetails.
            return string.Equals(result.Status, "Pending", StringComparison.OrdinalIgnoreCase)
                ? Results.Accepted($"/api/v1/sales/{result.SaleId}/mpesa/status", body)
                : Results.Ok(body);
        });

        // Poll: the till calls this on an interval until PaymentStatus is Confirmed or Failed.
        g.MapGet("/{saleId:guid}/status", async (
            Guid saleId,
            MpesaPaymentService mpesa,
            CancellationToken ct) =>
        {
            var s = await mpesa.QueryStatusAsync(saleId, ct);
            return Results.Ok(new MpesaStatusResponse(
                s.SaleId, s.CheckoutRequestId, s.PaymentStatus, s.SaleStatus,
                s.ResultDescription, s.Receipt, s.Total, s.ChangeDue, s.Currency));
        });

        return app;
    }

    /// <summary>
    /// Daraja result callback — mapped OUTSIDE the /api/v1 auth group because Safaricom can't send
    /// our identity headers. Reconciliation is by CheckoutRequestID + amount and is idempotent, so a
    /// replayed callback is safe. Always 200 with a Daraja-style ack so Safaricom stops retrying.
    /// </summary>
    public static IEndpointRouteBuilder MapMpesaCallback(this IEndpointRouteBuilder app)
    {
        app.MapPost("/mpesa/callback", async (
            MpesaCallbackEnvelope envelope,
            MpesaPaymentService mpesa,
            CancellationToken ct) =>
        {
            var stk = envelope.Body?.StkCallback;
            if (stk is null || string.IsNullOrWhiteSpace(stk.CheckoutRequestID))
                return Results.Ok(new { ResultCode = 0, ResultDesc = "Accepted (no stkCallback)." });

            var (amount, receipt) = ReadMetadata(stk.CallbackMetadata);
            var outcome = await mpesa.HandleCallbackAsync(new MpesaCallback(
                stk.CheckoutRequestID!, stk.MerchantRequestID, stk.ResultCode, stk.ResultDesc, amount, receipt), ct);

            return Results.Ok(new { ResultCode = 0, ResultDesc = outcome.Message });
        }).WithTags("M-Pesa");

        return app;
    }

    private static (decimal? Amount, string? Receipt) ReadMetadata(CallbackMetadata? metadata)
    {
        decimal? amount = null;
        string? receipt = null;
        foreach (var item in metadata?.Item ?? Array.Empty<CallbackItem>())
        {
            switch (item.Name)
            {
                case "Amount":
                    if (item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetDecimal(out var a)) amount = a;
                    else if (item.Value.ValueKind == JsonValueKind.String && decimal.TryParse(item.Value.GetString(), out var a2)) amount = a2;
                    break;
                case "MpesaReceiptNumber":
                    receipt = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : item.Value.ToString();
                    break;
            }
        }
        return (amount, receipt);
    }
}
