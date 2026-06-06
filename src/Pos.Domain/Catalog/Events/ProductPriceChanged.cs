using Pos.SharedKernel;

namespace Pos.Domain.Catalog.Events;

/// <summary>
/// Raised when a product's price actually changes. Drained to the outbox for audit and as the seam
/// for central pricing later (roadmap M2). The current Price still lives on the Product for fast
/// lookup; this records who changed it and from/to what (when = <see cref="DomainEvent.OccurredAtUtc"/>).
/// </summary>
public sealed record ProductPriceChanged(
    Guid ProductId,
    Guid TenantId,
    Guid StoreId,
    decimal OldAmount,
    decimal NewAmount,
    string Currency,
    Guid ChangedBy) : DomainEvent;
