namespace Pos.SharedKernel;

/// <summary>
/// INVARIANT #2 — store-authoritative ownership. Each branch's store server owns its
/// records; HQ aggregates. Carrying StoreId on every fact is what lets branches sync
/// to HQ without one branch overwriting another.
/// </summary>
public interface IStoreScoped
{
    Guid StoreId { get; }
}
