using Pos.SharedKernel;

namespace Pos.Domain.Inventory.Events;

/// <summary>
/// Raised when a source branch dispatches an inter-branch transfer (M3). The store→cloud sync ships it
/// up (hydrated to a full transfer snapshot); HQ routes it to the destination branch, which pulls and
/// applies the matching TransferIn. Carries the ids only; the push agent enriches it with the lines.
/// </summary>
public sealed record StockTransferDispatched(
    Guid TransferId,
    Guid TenantId,
    Guid FromStoreId,
    Guid ToStoreId) : DomainEvent;
