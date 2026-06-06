namespace Pos.Api.Contracts;

public sealed record StockOnHandResponse(Guid ProductId, decimal OnHand);
