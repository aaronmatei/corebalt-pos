using Pos.SharedKernel;

namespace Pos.Domain.Inventory;

public enum IncomingTransferStatus { Pending = 0, Received = 1 }

/// <summary>
/// Destination-side view of an inter-branch transfer (M3) pulled from HQ and awaiting receipt at this
/// store. Staged <see cref="IncomingTransferStatus.Pending"/> when pulled (no stock moves yet); a receiver
/// confirms a COUNTED quantity per line, at which point <see cref="StockMovementReason.TransferIn"/>
/// movements are written at the counted qty and the row flips to <see cref="IncomingTransferStatus.Received"/>.
/// Its existence + status ARE the receive idempotency marker — a re-pull of an already-received transfer is
/// a no-op re-ack, never a double-increment. One row per (tenant, store, transfer); the id IS the transfer
/// id (the client-generated UUIDv7 that's stable across both branches and HQ — INVARIANT #1/#2/#3).
/// </summary>
public sealed class IncomingTransfer : Entity, ITenantScoped, IStoreScoped
{
    private readonly List<IncomingTransferLine> _lines = new();

    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }          // the destination store (this branch)
    public Guid FromStoreId { get; private set; }
    public string FromStoreName { get; private set; } = string.Empty;
    public string DispatchedByName { get; private set; } = string.Empty;
    public DateTimeOffset DispatchedAtUtc { get; private set; }
    public string? Note { get; private set; }
    public IncomingTransferStatus Status { get; private set; }
    public DateTimeOffset? ReceivedAtUtc { get; private set; }
    public string? ReceivedByName { get; private set; }

    public IReadOnlyList<IncomingTransferLine> Lines => _lines.AsReadOnly();
    public bool HasDiscrepancy => _lines.Any(l => l.Discrepancy is { } d && d != 0m);

    private IncomingTransfer() { } // EF

    public static IncomingTransfer Stage(Guid tenantId, Guid storeId, Guid transferId, Guid fromStoreId,
        string fromStoreName, string dispatchedByName, DateTimeOffset dispatchedAtUtc, string? note,
        IEnumerable<(string Sku, string Name, decimal ExpectedQuantity)> lines)
    {
        if (transferId == Guid.Empty) throw new ArgumentException("Transfer id is required.", nameof(transferId));
        var t = new IncomingTransfer
        {
            Id = transferId,
            TenantId = tenantId,
            StoreId = storeId,
            FromStoreId = fromStoreId,
            FromStoreName = fromStoreName ?? string.Empty,
            DispatchedByName = dispatchedByName ?? string.Empty,
            DispatchedAtUtc = dispatchedAtUtc,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Status = IncomingTransferStatus.Pending,
        };
        foreach (var l in lines)
            t._lines.Add(new IncomingTransferLine(l.Sku, l.Name, l.ExpectedQuantity));
        if (t._lines.Count == 0) throw new InvalidOperationException("An incoming transfer needs at least one line.");
        return t;
    }

    /// <summary>
    /// Confirm receipt with the actually-counted quantity per line (keyed by line id). A line absent from the
    /// map defaults to its expected quantity (operator left it untouched). Only a Pending transfer can be
    /// received — a second call throws so a double-submit can't double-count.
    /// </summary>
    public void Receive(IReadOnlyDictionary<Guid, decimal> countedByLineId, string receivedByName, DateTimeOffset now)
    {
        if (Status == IncomingTransferStatus.Received)
            throw new InvalidOperationException("This transfer has already been received.");
        foreach (var line in _lines)
        {
            var counted = countedByLineId.TryGetValue(line.Id, out var q) ? q : line.ExpectedQuantity;
            if (counted < 0) throw new ArgumentException("Counted quantity cannot be negative.");
            line.SetReceived(counted);
        }
        Status = IncomingTransferStatus.Received;
        ReceivedAtUtc = now;
        ReceivedByName = string.IsNullOrWhiteSpace(receivedByName) ? "—" : receivedByName;
    }
}

public sealed class IncomingTransferLine : Entity
{
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal ExpectedQuantity { get; private set; }
    public decimal? ReceivedQuantity { get; private set; }
    /// <summary>Counted − expected once received (negative = short, positive = over); null while pending.</summary>
    public decimal? Discrepancy => ReceivedQuantity is { } r ? r - ExpectedQuantity : null;

    private IncomingTransferLine() { } // EF
    internal IncomingTransferLine(string sku, string name, decimal expected)
        : base(Pos.SharedKernel.Ids.Uuid7.NewGuid())
    {
        Sku = sku;
        Name = name;
        ExpectedQuantity = expected;
    }

    internal void SetReceived(decimal qty) => ReceivedQuantity = qty;
}
