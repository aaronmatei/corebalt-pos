using Pos.Domain.Inventory;

namespace Pos.Application.Inventory;

/// <summary>Destination-side store of inter-branch transfers pulled from HQ and awaiting / recording receipt (M3).</summary>
public interface IIncomingTransferRepository
{
    Task<IncomingTransfer?> GetAsync(Guid tenantId, Guid storeId, Guid transferId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid tenantId, Guid storeId, Guid transferId, CancellationToken ct = default);
    Task<IReadOnlyList<IncomingTransfer>> ListPendingAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);
    Task<IReadOnlyList<IncomingTransfer>> ListRecentAsync(Guid tenantId, Guid storeId, int take, CancellationToken ct = default);
    Task AddAsync(IncomingTransfer transfer, CancellationToken ct = default);
}
