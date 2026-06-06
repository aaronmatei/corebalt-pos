using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
using Pos.Domain.Catalog;
using Pos.Domain.Sales.Events;

namespace Pos.Domain.Sales;

/// <summary>
/// A sale is the unit of checkout. Once Completed it is an immutable fact (INVARIANT #3):
/// we never edit a finished sale or overwrite a stock figure — corrections happen as new
/// records (refunds, stock movements). Completing a sale RAISES a domain event that the
/// outbox/sync layer (step 2+) will propagate to HQ.
/// </summary>
public sealed class Sale : AggregateRoot, ITenantScoped, IStoreScoped, IAuditable
{
    private readonly List<SaleLine> _lines = new();
    private readonly List<Tender> _tenders = new();
    private readonly List<SaleVatSummaryLine> _vatSummary = new();

    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }     // the branch that owns this sale
    public Guid RegisterId { get; private set; }  // the lane / till
    public Guid CashierId { get; private set; }
    public SaleStatus Status { get; private set; }
    public string Currency { get; private set; }

    /// <summary>
    /// Human-readable, store-authoritative receipt number (e.g. "MB-000123"), stamped in the checkout
    /// transaction from a per-(tenant,store) sequence. Distinct from <see cref="Entity.Id"/> (the
    /// UUIDv7 stays the internal id / idempotency key). Null until the sale completes.
    /// </summary>
    public string? ReceiptNumber { get; private set; }

    public IReadOnlyList<SaleLine> Lines => _lines.AsReadOnly();
    public IReadOnlyList<Tender> Tenders => _tenders.AsReadOnly();
    public IReadOnlyList<SaleVatSummaryLine> VatSummary => _vatSummary.AsReadOnly();

    // Frozen at completion: the VAT-inclusive grand total (the immutable fact the receipt prints).
    public Money GrandTotal { get; private set; } = Money.Zero();

    // eTIMS fiscal fields — NULL until the Tax module transmits the sale to KRA. CUIN, signature and
    // the QR are minted by eTIMS on transmission and cannot be generated locally.
    public string? EtimsCuin { get; private set; }
    public string? EtimsSignature { get; private set; }
    public string? EtimsQrUrl { get; private set; }
    public DateTimeOffset? EtimsTransmittedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    private Sale() { Currency = "KES"; } // EF

    public static Sale Start(Guid tenantId, Guid storeId, Guid registerId, Guid cashierId, string currency = "KES") => new()
    {
        Id = Uuid7.NewGuid(),
        TenantId = tenantId,
        StoreId = storeId,
        RegisterId = registerId,
        CashierId = cashierId,
        Status = SaleStatus.Open,
        Currency = currency,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        CreatedBy = cashierId
    };

    public Money Subtotal => _lines.Aggregate(Money.Zero(Currency), (sum, l) => sum.Add(l.LineTotal));
    // Only CONFIRMED tenders count as paid — a pending M-Pesa STK push is not money in hand.
    public Money Paid => _tenders.Where(t => t.IsConfirmed).Aggregate(Money.Zero(Currency), (sum, t) => sum.Add(t.Amount));
    public Money BalanceDue => Subtotal.Subtract(Paid);
    public bool HasPendingTenders => _tenders.Any(t => t.IsPending);

    public void AddLine(Guid productId, string description, decimal quantity, Money unitPrice,
        TaxClass taxClass = TaxClass.StandardRated, UnitOfMeasure unitOfMeasure = UnitOfMeasure.Each)
    {
        EnsureOpen();
        if (unitPrice.Currency != Currency) throw new InvalidOperationException("Line currency mismatch.");
        _lines.Add(new SaleLine(Uuid7.NewGuid(), productId, description, quantity, unitPrice, taxClass, unitOfMeasure));
        Touch();
    }

    /// <summary>Add a synchronously-confirmed tender (cash, or a manually-keyed M-Pesa code).</summary>
    public void AddTender(TenderType type, Money amount, string? reference = null)
    {
        EnsureOpen();
        if (amount.Currency != Currency) throw new InvalidOperationException("Tender currency mismatch.");
        _tenders.Add(new Tender(Uuid7.NewGuid(), type, amount, TenderStatus.Confirmed, reference));
        Touch();
    }

    /// <summary>
    /// Add an asynchronous, PENDING tender (an M-Pesa STK push). It does not count toward Paid
    /// until <see cref="ConfirmTender"/> is called. Returns the new tender id so the caller can
    /// correlate it with the provider request and later confirm/fail it.
    /// </summary>
    public Guid AddPendingTender(TenderType type, Money amount, string? providerReference = null)
    {
        EnsureOpen();
        if (amount.Currency != Currency) throw new InvalidOperationException("Tender currency mismatch.");
        var tender = new Tender(Uuid7.NewGuid(), type, amount, TenderStatus.Pending, reference: null, providerReference);
        _tenders.Add(tender);
        Touch();
        return tender.Id;
    }

    public void SetTenderProviderReference(Guid tenderId, string providerReference)
    {
        EnsureOpen();
        RequireTender(tenderId).SetProviderReference(providerReference);
        Touch();
    }

    public void ConfirmTender(Guid tenderId, string? reference = null)
    {
        EnsureOpen();
        RequireTender(tenderId).Confirm(reference);
        Touch();
    }

    public void FailTender(Guid tenderId)
    {
        EnsureOpen();
        RequireTender(tenderId).Fail();
        Touch();
    }

    /// <summary>
    /// Stamp the human-readable receipt number, once, while the sale is still open (it's then frozen
    /// by Complete()). The caller supplies the formatted value from the store's sequence.
    /// </summary>
    public void AssignReceiptNumber(string receiptNumber)
    {
        EnsureOpen();
        if (!string.IsNullOrEmpty(ReceiptNumber))
            throw new InvalidOperationException("Receipt number is already assigned.");
        if (string.IsNullOrWhiteSpace(receiptNumber))
            throw new ArgumentException("Receipt number is required.", nameof(receiptNumber));
        ReceiptNumber = receiptNumber;
        Touch();
    }

    public void Park()   { EnsureOpen(); Status = SaleStatus.Parked; Touch(); }
    public void Resume() { if (Status != SaleStatus.Parked) throw new InvalidOperationException("Only parked sales can resume."); Status = SaleStatus.Open; Touch(); }

    /// <summary>
    /// Finalize the sale. Refuses while any tender is still Pending (money in flight) and while
    /// the confirmed total doesn't cover the basket. Once Completed the sale is immutable
    /// (INVARIANT #3) — corrections are new records, never edits.
    /// </summary>
    public void Complete()
    {
        EnsureOpen();
        if (_lines.Count == 0) throw new InvalidOperationException("Cannot complete an empty sale.");
        if (HasPendingTenders) throw new InvalidOperationException("Sale has a pending tender; confirm or fail it before completing.");
        if (BalanceDue.Amount > 0) throw new InvalidOperationException("Sale is not fully paid.");

        FreezeTax();
        Status = SaleStatus.Completed;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        Touch();
        Raise(new SaleCompleted(Id, TenantId, StoreId, Subtotal.Amount, Currency));
    }

    /// <summary>
    /// Back VAT out of every line, then store the per-class VAT summary + grand total as immutable
    /// facts. The summary is the SUM of the per-line figures so the receipt reconciles exactly; the
    /// classes are ordered by enum value so reprints are byte-identical.
    /// </summary>
    private void FreezeTax()
    {
        foreach (var line in _lines) line.FinalizeTax();

        _vatSummary.Clear();
        foreach (var group in _lines.GroupBy(l => l.TaxClass).OrderBy(g => (int)g.Key))
        {
            var taxable = group.Aggregate(Money.Zero(Currency), (sum, l) => sum.Add(l.TaxableAmount));
            var vat = group.Aggregate(Money.Zero(Currency), (sum, l) => sum.Add(l.VatAmount));
            _vatSummary.Add(new SaleVatSummaryLine(group.Key, taxable, vat));
        }

        GrandTotal = Subtotal;
    }

    /// <summary>True when the sale can be finalized now (no pending tenders and fully paid).</summary>
    public bool IsFullyPaid => !HasPendingTenders && BalanceDue.Amount <= 0;

    private Tender RequireTender(Guid tenderId) =>
        _tenders.FirstOrDefault(t => t.Id == tenderId)
        ?? throw new InvalidOperationException($"Tender {tenderId} not found on sale {Id}.");

    private void EnsureOpen()
    {
        if (Status != SaleStatus.Open)
            throw new InvalidOperationException($"Sale is {Status}; only Open sales can be modified.");
    }

    private void Touch() => UpdatedAtUtc = DateTimeOffset.UtcNow;
}
