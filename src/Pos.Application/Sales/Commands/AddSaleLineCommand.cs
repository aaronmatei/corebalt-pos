namespace Pos.Application.Sales.Commands;

public sealed record AddSaleLineCommand(
    Guid SaleId,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice);
