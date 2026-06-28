using Pos.SharedKernel;

namespace Pos.Domain.Inventory.Events;

/// <summary>
/// Raised when an append-only stock fact is recorded (receipt, sale deduction, adjustment, return). This
/// is what lets the HQ/cloud tier reconstruct stock-on-hand as the SUM of deltas. Carries only the ids +
/// delta; the push agent enriches it with the product's Sku/Name for the cloud view.
/// </summary>
public sealed record StockMovementRecorded(
    Guid MovementId,
    Guid TenantId,
    Guid StoreId,
    Guid ProductId,
    decimal QuantityDelta,
    StockMovementReason Reason) : DomainEvent;
