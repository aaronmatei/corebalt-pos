namespace Pos.Infrastructure.Persistence;

/// <summary>
/// Per-(tenant, store) receipt counter — the store-authoritative source of human-readable receipt
/// numbers. Incremented atomically via a raw upsert (see ReceiptNumberSequence); never EF-tracked.
/// </summary>
internal sealed class ReceiptCounter
{
    public Guid TenantId { get; set; }
    public Guid StoreId { get; set; }
    public long NextValue { get; set; }
}
