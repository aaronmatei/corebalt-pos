namespace Pos.Application.Sales.Commands;

public sealed record CompleteSaleCommand(Guid SaleId);

public sealed record CompleteSaleResult(Guid SaleId, decimal Total, decimal ChangeDue, string Currency);
