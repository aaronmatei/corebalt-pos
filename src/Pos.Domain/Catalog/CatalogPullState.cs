using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Catalog;

/// <summary>
/// A store server's cursor into the HQ catalogue change feed (M2): the highest <c>Seq</c> it has applied.
/// One row per (tenant, store). Lets the store catch up incrementally and idempotently — re-pulling from
/// the same cursor just re-applies the same upserts.
/// </summary>
public sealed class CatalogPullState : Entity, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public long LastSeq { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private CatalogPullState() { } // EF

    public static CatalogPullState Start(Guid tenantId, Guid storeId) =>
        new() { Id = Uuid7.NewGuid(), TenantId = tenantId, StoreId = storeId, LastSeq = 0 };

    public void Advance(long seq, DateTimeOffset now)
    {
        if (seq > LastSeq) LastSeq = seq;
        UpdatedAtUtc = now;
    }
}
