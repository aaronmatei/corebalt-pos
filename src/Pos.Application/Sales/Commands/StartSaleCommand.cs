namespace Pos.Application.Sales.Commands;

public sealed record StartSaleCommand(Guid RegisterId, string Currency = "KES");

public sealed record StartSaleResult(Guid SaleId);
