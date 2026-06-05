using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
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

    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }     // the branch that owns this sale
    public Guid RegisterId { get; private set; }  // the lane / till
    public Guid CashierId { get; private set; }
    public SaleStatus Status { get; private set; }
    public string Currency { get; private set; }

    public IReadOnlyList<SaleLine> Lines => _lines.AsReadOnly();
    public IReadOnlyList<Tender> Tenders => _tenders.AsReadOnly();

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
    public Money Paid     => _tenders.Aggregate(Money.Zero(Currency), (sum, t) => sum.Add(t.Amount));
    public Money BalanceDue => Subtotal.Subtract(Paid);

    public void AddLine(Guid productId, string description, decimal quantity, Money unitPrice)
    {
        EnsureOpen();
        if (unitPrice.Currency != Currency) throw new InvalidOperationException("Line currency mismatch.");
        _lines.Add(new SaleLine(Uuid7.NewGuid(), productId, description, quantity, unitPrice));
        Touch();
    }

    public void AddTender(TenderType type, Money amount, string? reference = null)
    {
        EnsureOpen();
        if (amount.Currency != Currency) throw new InvalidOperationException("Tender currency mismatch.");
        _tenders.Add(new Tender(Uuid7.NewGuid(), type, amount, reference));
        Touch();
    }

    public void Park()   { EnsureOpen(); Status = SaleStatus.Parked; Touch(); }
    public void Resume() { if (Status != SaleStatus.Parked) throw new InvalidOperationException("Only parked sales can resume."); Status = SaleStatus.Open; Touch(); }

    public void Complete()
    {
        EnsureOpen();
        if (_lines.Count == 0) throw new InvalidOperationException("Cannot complete an empty sale.");
        if (BalanceDue.Amount > 0) throw new InvalidOperationException("Sale is not fully paid.");
        Status = SaleStatus.Completed;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        Touch();
        Raise(new SaleCompleted(Id, TenantId, StoreId, Subtotal.Amount, Currency));
    }

    private void EnsureOpen()
    {
        if (Status != SaleStatus.Open)
            throw new InvalidOperationException($"Sale is {Status}; only Open sales can be modified.");
    }

    private void Touch() => UpdatedAtUtc = DateTimeOffset.UtcNow;
}
