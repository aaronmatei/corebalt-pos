using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
using Pos.Domain.Inventory.Events;

namespace Pos.Domain.Inventory;

public enum TransferStatus { Dispatched = 0, Received = 1 }

/// <summary>
/// An inter-branch stock transfer (M3) — owned by the SOURCE branch. Dispatching it writes reversing
/// <see cref="StockMovementReason.TransferOut"/> movements (stock leaves the source) and is an immutable
/// fact; the destination branch records its own <see cref="StockMovementReason.TransferIn"/> when it
/// receives (neither branch overwrites the other — INVARIANT #2/#3). Dispatch raises
/// <see cref="StockTransferDispatched"/> which the store→cloud sync ships to HQ for routing to the
/// destination. The transfer id is client-generated (UUIDv7) so it's a stable key across both branches + HQ.
/// </summary>
public sealed class StockTransfer : AggregateRoot, ITenantScoped, IStoreScoped
{
    private readonly List<StockTransferLine> _lines = new();

    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }      // the owning (source) store — same as FromStoreId
    public Guid FromStoreId { get; private set; }
    public Guid ToStoreId { get; private set; }
    public string ToStoreName { get; private set; } = string.Empty;
    public TransferStatus Status { get; private set; }
    public Guid DispatchedBy { get; private set; }
    public string DispatchedByName { get; private set; } = string.Empty;
    public DateTimeOffset DispatchedAtUtc { get; private set; }
    public string? Note { get; private set; }

    public IReadOnlyList<StockTransferLine> Lines => _lines.AsReadOnly();

    private StockTransfer() { } // EF

    public static StockTransfer Dispatch(Guid tenantId, Guid fromStoreId, Guid toStoreId, string toStoreName,
        Guid dispatchedBy, string dispatchedByName,
        IEnumerable<(Guid ProductId, string Sku, string Name, decimal Quantity)> lines, string? note,
        Guid transferId = default)
    {
        if (fromStoreId == Guid.Empty || toStoreId == Guid.Empty) throw new ArgumentException("Source and destination stores are required.");
        if (fromStoreId == toStoreId) throw new InvalidOperationException("Cannot transfer to the same branch.");

        var transfer = new StockTransfer
        {
            Id = transferId == default ? Uuid7.NewGuid() : transferId,
            TenantId = tenantId,
            StoreId = fromStoreId,
            FromStoreId = fromStoreId,
            ToStoreId = toStoreId,
            ToStoreName = toStoreName ?? string.Empty,
            DispatchedBy = dispatchedBy,
            DispatchedByName = dispatchedByName ?? string.Empty,
            DispatchedAtUtc = DateTimeOffset.UtcNow,
            Status = TransferStatus.Dispatched,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };

        foreach (var l in lines)
        {
            if (l.Quantity <= 0) throw new ArgumentException("Transfer quantity must be positive.", nameof(lines));
            transfer._lines.Add(new StockTransferLine(Uuid7.NewGuid(), l.ProductId, l.Sku, l.Name, l.Quantity));
        }
        if (transfer._lines.Count == 0) throw new InvalidOperationException("A transfer needs at least one line.");

        transfer.Raise(new StockTransferDispatched(transfer.Id, tenantId, fromStoreId, toStoreId));
        return transfer;
    }
}

public sealed class StockTransferLine : Entity
{
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }

    private StockTransferLine() { } // EF
    internal StockTransferLine(Guid id, Guid productId, string sku, string name, decimal quantity) : base(id)
    {
        ProductId = productId;
        Sku = sku;
        Name = name;
        Quantity = quantity;
    }
}
