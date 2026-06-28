using Pos.SharedKernel;

namespace Pos.Domain.Hq;

/// <summary>
/// HQ/cloud read-model of a completed sale synced up from a branch. NOT an aggregate — it's a flat
/// projection the cloud back-office reports over, keyed by the original SaleId so re-projection is an
/// idempotent upsert. Line detail is kept as JSON (the header carries the totals the lists need).
/// Tenant+store scoped, so the Phase-1 query filter isolates it per tenant automatically.
/// </summary>
public sealed class HqSale : Entity, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public string? ReceiptNumber { get; private set; }
    public string RegisterName { get; private set; } = string.Empty;
    public string CashierName { get; private set; } = string.Empty;
    public Guid? CustomerId { get; private set; }
    public string Currency { get; private set; } = "KES";
    public decimal GrandTotal { get; private set; }
    public decimal TotalVat { get; private set; }
    public int LineCount { get; private set; }
    public string LinesJson { get; private set; } = "[]";
    public DateTimeOffset CompletedAtUtc { get; private set; }
    public DateTimeOffset SyncedAtUtc { get; private set; }

    private HqSale() { } // EF

    public static HqSale Create(Guid saleId, Guid tenantId, Guid storeId, string? receiptNumber,
        string registerName, string cashierName, Guid? customerId, string currency,
        decimal grandTotal, decimal totalVat, int lineCount, string linesJson,
        DateTimeOffset completedAtUtc, DateTimeOffset now)
    {
        var s = new HqSale { Id = saleId };
        s.Apply(tenantId, storeId, receiptNumber, registerName, cashierName, customerId, currency,
            grandTotal, totalVat, lineCount, linesJson, completedAtUtc, now);
        return s;
    }

    /// <summary>Overwrite with the latest snapshot (idempotent re-projection of the same sale).</summary>
    public void Apply(Guid tenantId, Guid storeId, string? receiptNumber, string registerName,
        string cashierName, Guid? customerId, string currency, decimal grandTotal, decimal totalVat,
        int lineCount, string linesJson, DateTimeOffset completedAtUtc, DateTimeOffset now)
    {
        TenantId = tenantId;
        StoreId = storeId;
        ReceiptNumber = receiptNumber;
        RegisterName = registerName;
        CashierName = cashierName;
        CustomerId = customerId;
        Currency = currency;
        GrandTotal = grandTotal;
        TotalVat = totalVat;
        LineCount = lineCount;
        LinesJson = linesJson;
        CompletedAtUtc = completedAtUtc;
        SyncedAtUtc = now;
    }
}
