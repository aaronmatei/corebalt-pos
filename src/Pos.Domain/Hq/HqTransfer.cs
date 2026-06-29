using Pos.SharedKernel;

namespace Pos.Domain.Hq;

/// <summary>
/// HQ/cloud view of an inter-branch transfer (M3), routed from the source branch to the destination.
/// Keyed by the original transfer id; the source's dispatch creates it (Dispatched) and the destination's
/// receipt ack flips it to Received. Tenant scoped. Lines kept as JSON (the header drives the lists).
/// </summary>
public sealed class HqTransfer : Entity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid FromStoreId { get; private set; }
    public Guid ToStoreId { get; private set; }
    public string ToStoreName { get; private set; } = string.Empty;
    public string DispatchedByName { get; private set; } = string.Empty;
    public DateTimeOffset DispatchedAtUtc { get; private set; }
    public bool IsReceived { get; private set; }
    public DateTimeOffset? ReceivedAtUtc { get; private set; }
    public string? Note { get; private set; }
    public int LineCount { get; private set; }
    public string LinesJson { get; private set; } = "[]";
    public DateTimeOffset SyncedAtUtc { get; private set; }

    private HqTransfer() { } // EF

    public static HqTransfer Create(Guid transferId, Guid tenantId, Guid fromStoreId, Guid toStoreId,
        string toStoreName, string dispatchedByName, DateTimeOffset dispatchedAtUtc, string? note,
        int lineCount, string linesJson, DateTimeOffset now) => new()
    {
        Id = transferId,
        TenantId = tenantId,
        FromStoreId = fromStoreId,
        ToStoreId = toStoreId,
        ToStoreName = toStoreName,
        DispatchedByName = dispatchedByName,
        DispatchedAtUtc = dispatchedAtUtc,
        Note = note,
        LineCount = lineCount,
        LinesJson = linesJson,
        IsReceived = false,
        SyncedAtUtc = now,
    };

    public void MarkReceived(DateTimeOffset now)
    {
        if (IsReceived) return;
        IsReceived = true;
        ReceivedAtUtc = now;
        SyncedAtUtc = now;
    }
}
