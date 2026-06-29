using Pos.Domain.Inventory;

namespace Pos.Application.Inventory;

/// <summary>Destination-side dedup of applied inter-branch transfers (M3).</summary>
public interface IReceivedTransferRepository
{
    Task<bool> ExistsAsync(Guid tenantId, Guid storeId, Guid transferId, CancellationToken ct = default);
    Task AddAsync(ReceivedTransfer marker, CancellationToken ct = default);
}
