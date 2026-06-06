using Pos.Domain.Inventory;

namespace Pos.Application.Inventory.Commands;

public sealed record RecordStockMovementCommand(
    Guid ProductId,
    decimal QuantityDelta,
    StockMovementReason Reason,
    Guid? SourceRef = null);

public sealed record RecordStockMovementResult(Guid MovementId);
