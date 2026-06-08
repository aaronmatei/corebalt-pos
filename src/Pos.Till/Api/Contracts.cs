namespace Pos.Till.Api;

// Wire DTOs — the till's OWN copy of the API's JSON shapes. Deliberately NOT shared with
// Pos.Domain/Pos.Api: the till is a pure HTTP client and must compile with zero reference to
// the server assemblies. Enums mirror the server's names; the API serializes them as strings,
// so a JsonStringEnumConverter (configured in PosApiClient) keeps both ends in sync by name.

public sealed record TokenDto(string AccessToken, DateTimeOffset ExpiresAtUtc, string TokenType);

/// <summary>Fingerprint sign-in result: the session token + the resolved cashier label for the header.</summary>
public sealed record FingerprintLoginDto(string AccessToken, DateTimeOffset ExpiresAtUtc, string StaffCode, string Name, string TokenType);

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
    string? Barcode,
    Guid? CategoryId);

public sealed record CategoryDto(Guid Id, string Name, Guid? ParentId, int DisplayOrder, bool IsActive);

public sealed record CheckoutLineDto(Guid ProductId, decimal Quantity);

public sealed record CheckoutTenderDto(TenderType Type, decimal Amount, string? Reference);

public sealed record CheckoutRequestDto(
    Guid RegisterId,
    IReadOnlyList<CheckoutLineDto> Lines,
    IReadOnlyList<CheckoutTenderDto> Tenders,
    string Currency,
    // Edge-generated UUIDv7 the till stamps on the sale BEFORE sending, so a sale queued while offline
    // replays idempotently on reconnect (the server returns the committed sale, never a duplicate).
    Guid SaleId,
    // Optional loyalty member (null = walk-in); travels in the offline payload too so points accrue on sync.
    Guid? CustomerId = null);

public sealed record CustomerDto(Guid Id, string Name, string? Phone, string? Email, string? KraPin, string? NationalId, int LoyaltyPoints, bool IsActive);

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

// ── Cash management / shifts ──────────────────────────────────────────────────────────────────
public sealed record OpenSessionRequestDto(Guid RegisterId, decimal OpeningFloat);
public sealed record CashMovementRequestDto(Guid RegisterId, string Type, decimal Amount, string? Reason);
public sealed record CloseSessionRequestDto(decimal CountedCash, bool Acknowledged);

public sealed record SessionDto(
    Guid Id, Guid RegisterId, string RegisterLabel, string Status,
    string OpenedBy, string OpenedAtEat, decimal OpeningFloat,
    string? ClosedBy, string? ClosedAtEat,
    decimal? CountedCash, decimal? ExpectedCash, decimal? Variance, string Currency);

// Only the fields the till needs off the report projection (extra JSON is ignored on deserialize).
public sealed record ReportCashDto(decimal OpeningFloat, decimal CashSales, decimal CashRefunds,
    decimal PayIns, decimal PayOuts, decimal Drops, decimal Expected, decimal? Counted, decimal? Variance);
public sealed record ReportBodyDto(string Kind, ReportCashDto Cash);
public sealed record ShiftReportDto(ReportBodyDto Report, string Text);

/// <summary>RFC 7807 ProblemDetails — what the API returns for 400/401/409.</summary>
public sealed record ProblemDto(string? Title, string? Detail, int? Status);

/// <summary>A typed result so the UI can show the server's message instead of throwing.</summary>
public sealed record ApiResult<T>(bool Ok, T? Value, int StatusCode, string? Error)
{
    public static ApiResult<T> Success(T value) => new(true, value, 200, null);
    public static ApiResult<T> Failure(int status, string error) => new(false, default, status, error);
}
