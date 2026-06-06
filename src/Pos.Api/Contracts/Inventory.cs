using Pos.Domain.Catalog;
using Pos.Domain.Inventory;

namespace Pos.Api.Contracts;

public sealed record StockOnHandResponse(Guid ProductId, decimal OnHand);

/// <summary>Receive stock IN: a positive quantity with a reason (Purchase/OpeningBalance/Adjustment).</summary>
public sealed record ReceiveStockRequest(Guid ProductId, decimal Quantity, StockMovementReason Reason, string? Reference = null);

/// <summary>Stock adjustment: a SIGNED quantity (stock take / shrinkage), always reason Adjustment.</summary>
public sealed record AdjustStockRequest(Guid ProductId, decimal Quantity, string? Reference = null);

/// <summary>The immutable movement just written, plus the resulting on-hand (derived).</summary>
public sealed record StockMovementResponse(
    Guid Id, Guid ProductId, decimal QuantityDelta, StockMovementReason Reason, string? Reference, decimal OnHand);

public sealed record StockReportRow(Guid ProductId, string Sku, string Name, UnitOfMeasure UnitOfMeasure, bool IsActive, decimal OnHand);

public sealed record StockReportResponse(IReadOnlyList<StockReportRow> Items);
