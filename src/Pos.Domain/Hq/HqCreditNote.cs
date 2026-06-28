using Pos.SharedKernel;

namespace Pos.Domain.Hq;

/// <summary>HQ/cloud read-model of a return/refund (credit note) synced from a branch. Keyed by the
/// original CreditNoteId; idempotent upsert. Tenant+store scoped.</summary>
public sealed class HqCreditNote : Entity, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public string? ReturnNumber { get; private set; }
    public Guid OriginalSaleId { get; private set; }
    public string OriginalReceiptNumber { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public bool IsVoid { get; private set; }
    public string AuthorizedByName { get; private set; } = string.Empty;
    public string RefundMethod { get; private set; } = string.Empty;
    public string RefundStatus { get; private set; } = string.Empty;
    public decimal GrandTotal { get; private set; }
    public string Currency { get; private set; } = "KES";
    public int LineCount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset SyncedAtUtc { get; private set; }

    private HqCreditNote() { } // EF

    public static HqCreditNote Create(Guid creditNoteId, DateTimeOffset now) => new() { Id = creditNoteId, SyncedAtUtc = now };

    public void Apply(Guid tenantId, Guid storeId, string? returnNumber, Guid originalSaleId,
        string originalReceiptNumber, string reason, bool isVoid, string authorizedByName,
        string refundMethod, string refundStatus, decimal grandTotal, string currency, int lineCount,
        DateTimeOffset createdAtUtc, DateTimeOffset now)
    {
        TenantId = tenantId;
        StoreId = storeId;
        ReturnNumber = returnNumber;
        OriginalSaleId = originalSaleId;
        OriginalReceiptNumber = originalReceiptNumber;
        Reason = reason;
        IsVoid = isVoid;
        AuthorizedByName = authorizedByName;
        RefundMethod = refundMethod;
        RefundStatus = refundStatus;
        GrandTotal = grandTotal;
        Currency = currency;
        LineCount = lineCount;
        CreatedAtUtc = createdAtUtc;
        SyncedAtUtc = now;
    }
}
