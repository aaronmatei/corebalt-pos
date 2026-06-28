using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Hq;

/// <summary>
/// HQ/cloud read-model of stock-on-hand per (tenant, store, product): a RUNNING SUM of the synced
/// movement deltas — preserving INVARIANT #3 (on-hand is derived from append-only facts) on the cloud
/// side too. Surrogate <see cref="Entity.Id"/> with a unique (tenant, store, product) index; each synced
/// StockMovementRecorded adds its delta exactly once (the sync_inbox dedup guarantees once-only projection).
/// </summary>
public sealed class HqStockOnHand : Entity, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string UnitOfMeasure { get; private set; } = string.Empty;
    public decimal OnHand { get; private set; }
    public DateTimeOffset LastMovementAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private HqStockOnHand() { } // EF

    public static HqStockOnHand Create(Guid tenantId, Guid storeId, Guid productId) => new()
    {
        Id = Uuid7.NewGuid(),
        TenantId = tenantId,
        StoreId = storeId,
        ProductId = productId,
        OnHand = 0m,
    };

    /// <summary>Apply one synced movement: add its delta and refresh the denormalized product label.</summary>
    public void AddDelta(decimal delta, string sku, string name, string unitOfMeasure,
        DateTimeOffset occurredAtUtc, DateTimeOffset now)
    {
        OnHand += delta;
        if (!string.IsNullOrWhiteSpace(sku)) Sku = sku;
        if (!string.IsNullOrWhiteSpace(name)) Name = name;
        if (!string.IsNullOrWhiteSpace(unitOfMeasure)) UnitOfMeasure = unitOfMeasure;
        if (occurredAtUtc > LastMovementAtUtc) LastMovementAtUtc = occurredAtUtc;
        UpdatedAtUtc = now;
    }
}
