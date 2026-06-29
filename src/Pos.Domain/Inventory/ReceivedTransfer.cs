using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Inventory;

/// <summary>
/// Destination-side dedup marker (M3): records that this store already applied a given inter-branch
/// transfer (wrote its TransferIn movements). Lets the receiver re-pull/re-ack safely without
/// double-incrementing stock if an ack was lost. One row per (tenant, store, transfer).
/// </summary>
public sealed class ReceivedTransfer : Entity, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid TransferId { get; private set; }
    public DateTimeOffset AppliedAtUtc { get; private set; }

    private ReceivedTransfer() { } // EF

    public static ReceivedTransfer Mark(Guid tenantId, Guid storeId, Guid transferId, DateTimeOffset now) => new()
    {
        Id = Uuid7.NewGuid(),
        TenantId = tenantId,
        StoreId = storeId,
        TransferId = transferId,
        AppliedAtUtc = now,
    };
}
