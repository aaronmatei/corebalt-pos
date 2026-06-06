using Pos.Domain.Sales;

namespace Pos.Api.Contracts;

public sealed record StartSaleRequest(Guid RegisterId, string Currency = "KES");
public sealed record StartSaleResponse(Guid SaleId);

public sealed record AddLineRequest(Guid ProductId, decimal Quantity);

public sealed record AddTenderRequest(TenderType Type, decimal Amount, string? Reference = null);

// One-shot checkout: the till sends the whole basket + tenders in a single atomic request.
public sealed record CheckoutLineRequest(Guid ProductId, decimal Quantity);
public sealed record CheckoutTenderRequest(TenderType Type, decimal Amount, string? Reference = null);
public sealed record CheckoutRequest(
    Guid RegisterId,
    IReadOnlyList<CheckoutLineRequest> Lines,
    IReadOnlyList<CheckoutTenderRequest> Tenders,
    string Currency = "KES");

public sealed record CompleteSaleResponse(Guid SaleId, decimal Total, decimal ChangeDue, string Currency);

public sealed record SaleLineResponse(
    Guid Id,
    Guid ProductId,
    string Description,
    decimal Quantity,
    MoneyDto UnitPrice,
    MoneyDto LineTotal);

public sealed record TenderResponse(Guid Id, TenderType Type, MoneyDto Amount, string? Reference);

public sealed record SaleResponse(
    Guid Id,
    SaleStatus Status,
    string Currency,
    IReadOnlyList<SaleLineResponse> Lines,
    IReadOnlyList<TenderResponse> Tenders,
    MoneyDto Subtotal,
    MoneyDto Paid,
    MoneyDto BalanceDue,
    DateTimeOffset? CompletedAtUtc);
