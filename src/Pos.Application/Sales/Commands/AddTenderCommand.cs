using Pos.Domain.Sales;

namespace Pos.Application.Sales.Commands;

public sealed record AddTenderCommand(
    Guid SaleId,
    TenderType Type,
    decimal Amount,
    string? Reference = null);
