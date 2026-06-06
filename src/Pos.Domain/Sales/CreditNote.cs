using Pos.Domain.Catalog;
using Pos.Domain.Sales.Events;
using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Sales;

/// <summary>
/// A return / void: a NEW immutable transaction that reverses (all or part of) a completed Sale. It
/// references the original sale (never mutates it), holds the returned lines (original price + tax),
/// a reason, the authorizing user, and the refund. A "void" is simply a full-quantity return. Returned
/// goods are reversed by NEW stock-IN movements written alongside (on-hand stays derived).
/// </summary>
public sealed class CreditNote : AggregateRoot, ITenantScoped, IStoreScoped
{
    private readonly List<CreditNoteLine> _lines = new();

    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }

    public Guid OriginalSaleId { get; private set; }
    public string OriginalReceiptNumber { get; private set; }
    public string? OriginalEtimsCuin { get; private set; }   // the original receipt's CUIN, referenced by the credit note

    public ReturnReason Reason { get; private set; }
    public Guid AuthorizedBy { get; private set; }
    public string AuthorizedByName { get; private set; }
    public string AuthorizedByStaffCode { get; private set; }

    public string Currency { get; private set; }
    public RefundMethod RefundMethod { get; private set; }
    public Money RefundAmount { get; private set; }
    public RefundStatus RefundStatus { get; private set; }

    public string? ReturnNumber { get; private set; }        // human, store-authoritative (e.g. "MB-CN-000124")
    public Money GrandTotal { get; private set; }            // positive magnitude refunded
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // eTIMS credit-note fiscal fields (stub via the same provider seam).
    public FiscalStatus FiscalStatus { get; private set; } = FiscalStatus.NotRequired;
    public string? EtimsCuin { get; private set; }
    public string? EtimsSignature { get; private set; }
    public string? EtimsQrUrl { get; private set; }
    public DateTimeOffset? EtimsSignedAtUtc { get; private set; }

    public IReadOnlyList<CreditNoteLine> Lines => _lines.AsReadOnly();
    public bool IsFiscalized => EtimsCuin is not null;

    private CreditNote()
    {
        Currency = "KES";
        OriginalReceiptNumber = string.Empty;
        AuthorizedByName = string.Empty;
        AuthorizedByStaffCode = string.Empty;
        RefundAmount = Money.Zero();
        GrandTotal = Money.Zero();
    } // EF

    public static CreditNote Create(Guid id, Sale originalSale, ReturnReason reason,
        Guid authorizedBy, string authorizedByName, string authorizedByStaffCode)
    {
        if (id == Guid.Empty) throw new ArgumentException("A client-generated return id is required.", nameof(id));
        if (originalSale.Status != SaleStatus.Completed)
            throw new InvalidOperationException("Only a completed sale can be returned.");

        return new CreditNote
        {
            Id = id,
            TenantId = originalSale.TenantId,
            StoreId = originalSale.StoreId,
            OriginalSaleId = originalSale.Id,
            OriginalReceiptNumber = originalSale.ReceiptNumber ?? originalSale.Id.ToString(),
            OriginalEtimsCuin = originalSale.EtimsCuin,
            Reason = reason,
            AuthorizedBy = authorizedBy,
            AuthorizedByName = authorizedByName ?? string.Empty,
            AuthorizedByStaffCode = authorizedByStaffCode ?? string.Empty,
            Currency = originalSale.Currency,
            RefundMethod = RefundMethod.Cash,
            RefundAmount = Money.Zero(originalSale.Currency),
            RefundStatus = RefundStatus.Refunded,
            GrandTotal = Money.Zero(originalSale.Currency),
            FiscalStatus = FiscalStatus.NotRequired,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public void AddLine(Guid productId, string description, decimal quantity, Money unitPrice,
        TaxClass taxClass, UnitOfMeasure unitOfMeasure)
    {
        if (ReturnNumber is not null) throw new InvalidOperationException("Cannot add lines after the credit note is issued.");
        _lines.Add(new CreditNoteLine(Uuid7.NewGuid(), productId, description, quantity, unitPrice, taxClass, unitOfMeasure));
    }

    /// <summary>Finalize: stamp the return number, freeze totals + the refund, and raise the event.</summary>
    public void Issue(string returnNumber, RefundMethod refundMethod)
    {
        if (ReturnNumber is not null) throw new InvalidOperationException("Credit note already issued.");
        if (_lines.Count == 0) throw new InvalidOperationException("A return needs at least one line.");

        ReturnNumber = returnNumber;
        GrandTotal = _lines.Aggregate(Money.Zero(Currency), (sum, l) => sum.Add(l.LineTotal));
        RefundMethod = refundMethod;
        RefundAmount = new Money(GrandTotal.Amount, GrandTotal.Currency); // distinct owned instance from GrandTotal
        // Cash is paid out now; M-Pesa/other are flagged for a manual, out-of-band refund.
        RefundStatus = refundMethod == RefundMethod.Cash ? RefundStatus.Refunded : RefundStatus.PendingManual;

        Raise(new CreditNoteIssued(Id, TenantId, StoreId, OriginalSaleId, GrandTotal.Amount, Reason.ToString()));
    }

    public void ApplyFiscalSignature(string cuin, string signature, string qrData, DateTimeOffset signedAtUtc)
    {
        if (IsFiscalized) return;
        EtimsCuin = cuin;
        EtimsSignature = signature;
        EtimsQrUrl = qrData;
        EtimsSignedAtUtc = signedAtUtc;
        FiscalStatus = FiscalStatus.Signed;
    }

    public void MarkFiscalFailed() => FiscalStatus = FiscalStatus.Failed;
}
