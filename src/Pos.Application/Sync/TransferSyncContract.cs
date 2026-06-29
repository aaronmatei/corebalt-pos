namespace Pos.Application.Sync;

/// <summary>HQ→destination pull of incoming inter-branch transfers (M3). The destination applies each
/// and acks receipt; HQ then stops returning it.</summary>
public sealed record IncomingTransfersResponse(IReadOnlyList<TransferSnapshot> Transfers);

/// <summary>The tenant's OTHER branches (M3) — for the destination picker when dispatching a transfer.</summary>
public sealed record BranchesResponse(IReadOnlyList<BranchDto> Branches);

public sealed record BranchDto(Guid StoreId, string Name);
