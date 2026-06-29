using Pos.Domain.Inventory;

namespace Pos.Application.Inventory;

/// <summary>Inter-branch stock transfers owned by the source store (M3).</summary>
public interface ITransferRepository
{
    Task AddAsync(StockTransfer transfer, CancellationToken ct = default);
    Task<StockTransfer?> GetAsync(Guid tenantId, Guid storeId, Guid transferId, CancellationToken ct = default);
    Task<IReadOnlyList<StockTransfer>> ListRecentAsync(Guid tenantId, Guid storeId, int take, CancellationToken ct = default);
}
