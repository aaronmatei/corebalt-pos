namespace Pos.Till.Api;

// Wire DTOs — the till's OWN copy of the API's JSON shapes. Deliberately NOT shared with
// Pos.Domain/Pos.Api: the till is a pure HTTP client and must compile with zero reference to
// the server assemblies. Enums mirror the server's names; the API serializes them as strings,
// so a JsonStringEnumConverter (configured in PosApiClient) keeps both ends in sync by name.

public enum UnitOfMeasure { Each = 0, Kg = 1 }

public enum TenderType { Cash = 0, Mpesa = 1, Card = 2, AirtelMoney = 3 }

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    MoneyDto Price,
    UnitOfMeasure UnitOfMeasure,
    bool IsActive,
    string? Barcode);

public sealed record CheckoutLineDto(Guid ProductId, decimal Quantity);

public sealed record CheckoutTenderDto(TenderType Type, decimal Amount, string? Reference);

public sealed record CheckoutRequestDto(
    Guid RegisterId,
    IReadOnlyList<CheckoutLineDto> Lines,
    IReadOnlyList<CheckoutTenderDto> Tenders,
    string Currency);

public sealed record CompleteSaleDto(Guid SaleId, decimal Total, decimal ChangeDue, string Currency);

// ── M-Pesa (asynchronous) ───────────────────────────────────────────────────────────────────
public sealed record MpesaCheckoutRequestDto(
    Guid RegisterId,
    IReadOnlyList<CheckoutLineDto> Lines,
    decimal MpesaAmount,
    string PhoneNumber,
    string? AccountReference,
    IReadOnlyList<CheckoutTenderDto>? CashTenders,
    string Currency);

public sealed record MpesaInitiateDto(Guid SaleId, Guid TenderId, string? CheckoutRequestId, string Status, string? Message);

public sealed record MpesaStatusDto(
    Guid SaleId, string? CheckoutRequestId, string PaymentStatus, string SaleStatus,
    string? ResultDescription, string? Receipt, decimal? Total, decimal? ChangeDue, string Currency);

// Receipt — only the rendered text/html are needed for display (the API also returns the model).
public sealed record ReceiptDto(string Text, string Html, int Columns);

public sealed record SaleLineDto(
    Guid Id, Guid ProductId, string Description, decimal Quantity, MoneyDto UnitPrice, MoneyDto LineTotal);

public sealed record TenderDto(Guid Id, TenderType Type, MoneyDto Amount, string? Reference);

public sealed record SaleDto(
    Guid Id,
    string Status,
    string Currency,
    IReadOnlyList<SaleLineDto> Lines,
    IReadOnlyList<TenderDto> Tenders,
    MoneyDto Subtotal,
    MoneyDto Paid,
    MoneyDto BalanceDue,
    DateTimeOffset? CompletedAtUtc);

/// <summary>RFC 7807 ProblemDetails — what the API returns for 400/401/409.</summary>
public sealed record ProblemDto(string? Title, string? Detail, int? Status);

/// <summary>A typed result so the UI can show the server's message instead of throwing.</summary>
public sealed record ApiResult<T>(bool Ok, T? Value, int StatusCode, string? Error)
{
    public static ApiResult<T> Success(T value) => new(true, value, 200, null);
    public static ApiResult<T> Failure(int status, string error) => new(false, default, status, error);
}
