namespace Pos.Application.Sync;

/// <summary>HQ-side queries/commands for inter-branch transfer routing (M3), token-authed per store.</summary>
public interface IHqTransferStore
{
    /// <summary>Undelivered transfers destined for this store.</summary>
    Task<IReadOnlyList<TransferSnapshot>> IncomingAsync(Guid tenantId, Guid toStoreId, CancellationToken ct = default);

    /// <summary>Mark a transfer received (only if it was destined for this store). True if it existed.</summary>
    Task<bool> MarkReceivedAsync(Guid tenantId, Guid toStoreId, Guid transferId, CancellationToken ct = default);

    /// <summary>The tenant's other branches (for the destination picker).</summary>
    Task<IReadOnlyList<BranchDto>> BranchesAsync(Guid tenantId, Guid exceptStoreId, CancellationToken ct = default);
}
