using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
using Pos.Domain.Inventory.Events;

namespace Pos.Domain.Inventory;

/// <summary>
/// INVARIANT #3, applied to inventory: an immutable, append-only stock fact.
/// Stock-on-hand for a product at a store is the SUM of its movements — never a mutable
/// "quantity" column. This is precisely what lets many branches (and offline tills)
/// reconcile to HQ without last-write-wins corrupting counts. Recording a movement RAISES a
/// <see cref="StockMovementRecorded"/> domain event (outbox → HQ sync sums deltas into on-hand).
/// </summary>
public sealed class StockMovement : AggregateRoot, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal QuantityDelta { get; private set; } // + receipts, - sales; decimal for weighed goods
    public StockMovementReason Reason { get; private set; }
    public Guid? SourceRef { get; private set; }        // e.g. the SaleId that caused the movement
    public string? Reference { get; private set; }      // free-text: supplier / GRN / stock-take note
    public DateTimeOffset OccurredAtUtc { get; private set; }

    private StockMovement() { } // EF

    public static StockMovement Record(Guid tenantId, Guid storeId, Guid productId,
        decimal quantityDelta, StockMovementReason reason, Guid? sourceRef = null, string? reference = null)
    {
        if (quantityDelta == 0) throw new ArgumentException("Movement delta cannot be zero.", nameof(quantityDelta));
        var movement = new StockMovement
        {
            Id = Uuid7.NewGuid(),
            TenantId = tenantId,
            StoreId = storeId,
            ProductId = productId,
            QuantityDelta = quantityDelta,
            Reason = reason,
            SourceRef = sourceRef,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            OccurredAtUtc = DateTimeOffset.UtcNow
        };
        movement.Raise(new StockMovementRecorded(movement.Id, tenantId, storeId, productId, quantityDelta, reason));
        return movement;
    }
}
