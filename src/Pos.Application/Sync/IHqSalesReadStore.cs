namespace Pos.Application.Sync;

/// <summary>HQ/cloud back-office read of the synced sales read-model (<c>hq_sales</c>), per tenant.</summary>
public interface IHqSalesReadStore
{
    Task<HqSalesView> RecentAsync(Guid tenantId, int take, CancellationToken ct = default);
}

public sealed record HqSalesView(int Count, decimal Total, int StoreCount, IReadOnlyList<HqSaleRow> Sales);

public sealed record HqSaleRow(
    Guid Id, Guid StoreId, string? ReceiptNumber, string RegisterName, string CashierName,
    decimal GrandTotal, decimal TotalVat, int LineCount, string Currency,
    DateTimeOffset CompletedAtUtc, DateTimeOffset SyncedAtUtc);

/// <summary>HQ/cloud back-office read of the synced cash-up sessions (<c>hq_sessions</c>), per tenant.</summary>
public interface IHqSessionsReadStore
{
    Task<IReadOnlyList<HqSessionRow>> RecentAsync(Guid tenantId, int take, CancellationToken ct = default);
}

public sealed record HqSessionRow(
    Guid Id, Guid StoreId, Guid RegisterId, string RegisterLabel,
    string OpenedByName, DateTimeOffset OpenedAtUtc, decimal OpeningFloat,
    string? ClosedByName, DateTimeOffset? ClosedAtUtc,
    decimal CountedCash, decimal ExpectedCash, decimal Variance, bool VarianceAcknowledged, string Currency);

/// <summary>HQ/cloud read of synced returns/refunds (<c>hq_credit_notes</c>), per tenant.</summary>
public interface IHqCreditNotesReadStore
{
    Task<IReadOnlyList<HqCreditNoteRow>> RecentAsync(Guid tenantId, int take, CancellationToken ct = default);
}

public sealed record HqCreditNoteRow(
    Guid Id, Guid StoreId, string? ReturnNumber, string OriginalReceiptNumber, string Reason,
    bool IsVoid, string AuthorizedByName, string RefundMethod, string RefundStatus,
    decimal GrandTotal, string Currency, int LineCount, DateTimeOffset CreatedAtUtc);

/// <summary>HQ/cloud read of synced stock-on-hand (<c>hq_stock_on_hand</c>), per tenant.</summary>
public interface IHqStockReadStore
{
    Task<IReadOnlyList<HqStockRow>> AllAsync(Guid tenantId, int take, CancellationToken ct = default);
}

public sealed record HqStockRow(
    Guid StoreId, Guid ProductId, string Sku, string Name, string UnitOfMeasure,
    decimal OnHand, DateTimeOffset LastMovementAtUtc);

/// <summary>HQ/cloud read of store→cloud sync health (from <c>sync_inbox</c>), per tenant — so an operator
/// can see each branch is actually pushing and how fresh it is.</summary>
public interface IHqSyncStatusReadStore
{
    Task<HqSyncStatus> GetAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed record HqSyncStatus(int TotalChanges, DateTimeOffset? LastReceivedAtUtc, IReadOnlyList<HqStoreSyncRow> Stores);

public sealed record HqStoreSyncRow(
    Guid StoreId, int Changes, DateTimeOffset LastReceivedAtUtc, DateTimeOffset LastOccurredAtUtc);
