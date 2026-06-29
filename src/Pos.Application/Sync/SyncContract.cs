namespace Pos.Application.Sync;

/// <summary>Header names shared by both ends of the store→cloud sync.</summary>
public static class SyncHeaders
{
    public const string Token = "X-Sync-Token";
}

/// <summary>
/// Wire contract for the store→cloud sync push (on-prem store server → HQ <c>POST /hq/sync/ingest</c>).
/// One batch of outbox changes for a single store. The store presents its sync token in the
/// <c>X-Sync-Token</c> header; the cloud resolves <see cref="TenantSlug"/> → tenant and verifies it.
/// </summary>
public sealed record SyncIngestRequest(
    string TenantSlug,
    Guid StoreId,
    IReadOnlyList<SyncChangeDto> Changes,
    string? StoreName = null);   // M3: the store self-registers its branch name (for the HQ branch registry)

/// <summary>
/// One outbox change. <see cref="Id"/> is the original store-side outbox id and the idempotency key —
/// re-sending it is a no-op on the cloud. <see cref="Payload"/> is the raw domain-event JSON;
/// <see cref="Snapshot"/> is an optional hydrated read-model (e.g. a <see cref="SaleSnapshot"/> for a
/// SaleCompleted event, whose raw payload is too thin to project from).
/// </summary>
public sealed record SyncChangeDto(
    Guid Id,
    Guid AggregateId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset EnqueuedAtUtc,
    string Payload,
    string? Snapshot);

/// <summary>The ids the cloud durably accepted (stored in its inbox). The store acks exactly these.</summary>
public sealed record SyncIngestResponse(IReadOnlyList<Guid> AcceptedIds);

// ── Hydrated read-model snapshots (carried in SyncChangeDto.Snapshot) ──

/// <summary>A completed sale, fully hydrated store-side so the cloud can project it without the source DB.</summary>
public sealed record SaleSnapshot(
    Guid SaleId,
    Guid TenantId,
    Guid StoreId,
    string? ReceiptNumber,
    Guid RegisterId,
    string RegisterName,
    Guid CashierId,
    string CashierName,
    Guid? CustomerId,
    string Currency,
    decimal GrandTotal,
    decimal TotalVat,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<SaleLineSnapshot> Lines,
    IReadOnlyList<SaleTenderSnapshot> Tenders);

public sealed record SaleLineSnapshot(
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string TaxClass,
    decimal VatAmount);

public sealed record SaleTenderSnapshot(string Type, decimal Amount, string Status, string? Reference);

/// <summary>A dispatched inter-branch transfer (M3), hydrated store-side so HQ can route it.</summary>
public sealed record TransferSnapshot(
    Guid TransferId,
    Guid TenantId,
    Guid FromStoreId,
    Guid ToStoreId,
    string ToStoreName,
    string DispatchedByName,
    DateTimeOffset DispatchedAtUtc,
    string? Note,
    IReadOnlyList<TransferLineSnapshot> Lines);

public sealed record TransferLineSnapshot(Guid ProductId, string Sku, string Name, decimal Quantity);

/// <summary>A return/refund (credit note), hydrated store-side for the cloud's returns view.</summary>
public sealed record CreditNoteSnapshot(
    Guid CreditNoteId,
    Guid TenantId,
    Guid StoreId,
    string? ReturnNumber,
    Guid OriginalSaleId,
    string OriginalReceiptNumber,
    string Reason,
    bool IsVoid,
    string AuthorizedByName,
    string RefundMethod,
    string RefundStatus,
    decimal GrandTotal,
    string Currency,
    int LineCount,
    DateTimeOffset CreatedAtUtc);

/// <summary>One immutable stock movement (no hydration needed for the fact, but the pusher enriches it
/// with the product's Sku/Name so the cloud view is readable). The cloud SUMs deltas into on-hand.</summary>
public sealed record StockMovementSnapshot(
    Guid MovementId,
    Guid TenantId,
    Guid StoreId,
    Guid ProductId,
    string Sku,
    string Name,
    string UnitOfMeasure,
    decimal QuantityDelta,
    string Reason,
    DateTimeOffset OccurredAtUtc);

/// <summary>A closed cash-up shift (Z), hydrated store-side for the cloud's branch-takings rollup.</summary>
public sealed record SessionSnapshot(
    Guid SessionId,
    Guid TenantId,
    Guid StoreId,
    Guid RegisterId,
    string RegisterLabel,
    string OpenedByName,
    DateTimeOffset OpenedAtUtc,
    decimal OpeningFloat,
    string? ClosedByName,
    DateTimeOffset? ClosedAtUtc,
    decimal CountedCash,
    decimal ExpectedCash,
    decimal Variance,
    bool VarianceAcknowledged,
    string Currency);
