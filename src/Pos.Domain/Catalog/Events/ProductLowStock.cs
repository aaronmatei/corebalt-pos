using Pos.SharedKernel;

namespace Pos.Domain.Catalog.Events;

/// <summary>
/// Raised when a product's on-hand crosses DOWN to/below its reorder level (once per dip — see
/// <see cref="Product.EvaluateReorder"/>). Drained to the outbox by the same transaction that records the
/// stock movement, so checkout is never blocked. A back-office handler turns this into a notification
/// (in-app now; Email/SMS channels later). Carries enough to render the alert without a re-query: the
/// product identity, the on-hand at the crossing, the level, and the suggested order quantity.
/// </summary>
public sealed record ProductLowStock(
    Guid ProductId,
    Guid TenantId,
    Guid StoreId,
    string Sku,
    string Name,
    decimal OnHand,
    decimal ReorderLevel,
    decimal? ReorderQuantity,
    UnitOfMeasure UnitOfMeasure) : DomainEvent;
