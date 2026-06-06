using System.Text.Json;

namespace Pos.Api.Contracts;

// ── Till-facing M-Pesa request/response ────────────────────────────────────────────────────────

/// <summary>
/// Initiate an M-Pesa STK push for a basket. Optional CashTenders allow a split payment (cash now,
/// M-Pesa for the balance). The sale is created OPEN with a pending M-Pesa tender; it completes only
/// once the customer confirms.
/// </summary>
public sealed record MpesaCheckoutRequest(
    Guid RegisterId,
    IReadOnlyList<CheckoutLineRequest> Lines,
    decimal MpesaAmount,
    string PhoneNumber,
    string? AccountReference = null,
    IReadOnlyList<CheckoutTenderRequest>? CashTenders = null,
    string Currency = "KES");

public sealed record MpesaInitiateResponse(
    Guid SaleId, Guid TenderId, string? CheckoutRequestId, string Status, string? Message);

public sealed record MpesaStatusResponse(
    Guid SaleId, string? CheckoutRequestId, string PaymentStatus, string SaleStatus,
    string? ResultDescription, string? Receipt, decimal? Total, decimal? ChangeDue, string Currency);

// ── Daraja callback shape (what Safaricom POSTs to CallBackURL) ─────────────────────────────────

public sealed record MpesaCallbackEnvelope(MpesaCallbackBody? Body);
public sealed record MpesaCallbackBody(StkCallback? StkCallback);
public sealed record StkCallback(
    string? MerchantRequestID,
    string? CheckoutRequestID,
    int ResultCode,
    string? ResultDesc,
    CallbackMetadata? CallbackMetadata);
public sealed record CallbackMetadata(IReadOnlyList<CallbackItem>? Item);
public sealed record CallbackItem(string? Name, JsonElement Value);
