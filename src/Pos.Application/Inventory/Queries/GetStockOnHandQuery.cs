namespace Pos.Application.Inventory.Queries;

public sealed record GetStockOnHandQuery(Guid ProductId);

public sealed record GetStockOnHandResult(Guid ProductId, decimal OnHand);
