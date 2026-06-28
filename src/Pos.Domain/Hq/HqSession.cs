using Pos.SharedKernel;

namespace Pos.Domain.Hq;

/// <summary>
/// HQ/cloud read-model of a closed cash-up shift (Z) synced from a branch — the branch-takings rollup.
/// Keyed by the original SessionId so re-projection is an idempotent upsert. Tenant+store scoped.
/// </summary>
public sealed class HqSession : Entity, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid RegisterId { get; private set; }
    public string RegisterLabel { get; private set; } = string.Empty;
    public string OpenedByName { get; private set; } = string.Empty;
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public decimal OpeningFloat { get; private set; }
    public string? ClosedByName { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public decimal CountedCash { get; private set; }
    public decimal ExpectedCash { get; private set; }
    public decimal Variance { get; private set; }
    public bool VarianceAcknowledged { get; private set; }
    public string Currency { get; private set; } = "KES";
    public DateTimeOffset SyncedAtUtc { get; private set; }

    private HqSession() { } // EF

    public static HqSession Create(Guid sessionId, DateTimeOffset now)
    {
        var s = new HqSession { Id = sessionId };
        s.SyncedAtUtc = now;
        return s;
    }

    /// <summary>Overwrite with the latest snapshot (idempotent re-projection of the same session).</summary>
    public void Apply(Guid tenantId, Guid storeId, Guid registerId, string registerLabel,
        string openedByName, DateTimeOffset openedAtUtc, decimal openingFloat,
        string? closedByName, DateTimeOffset? closedAtUtc, decimal countedCash, decimal expectedCash,
        decimal variance, bool varianceAcknowledged, string currency, DateTimeOffset now)
    {
        TenantId = tenantId;
        StoreId = storeId;
        RegisterId = registerId;
        RegisterLabel = registerLabel;
        OpenedByName = openedByName;
        OpenedAtUtc = openedAtUtc;
        OpeningFloat = openingFloat;
        ClosedByName = closedByName;
        ClosedAtUtc = closedAtUtc;
        CountedCash = countedCash;
        ExpectedCash = expectedCash;
        Variance = variance;
        VarianceAcknowledged = varianceAcknowledged;
        Currency = currency;
        SyncedAtUtc = now;
    }
}
