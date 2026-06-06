using Pos.Domain.Catalog;
using Pos.SharedKernel;

namespace Pos.Domain.Sales;

/// <summary>
/// Per-tax-class VAT total for a completed sale (the fiscal breakdown printed on the receipt).
/// Stored at completion as the SUM of the per-line backed-out figures, so line items always
/// reconcile to the summary. One row per tax class present on the sale.
/// </summary>
public sealed class SaleVatSummaryLine
{
    public TaxClass TaxClass { get; private set; }
    public Money TaxableAmount { get; private set; }
    public Money VatAmount { get; private set; }

    private SaleVatSummaryLine()
    {
        TaxableAmount = Money.Zero();
        VatAmount = Money.Zero();
    } // EF

    internal SaleVatSummaryLine(TaxClass taxClass, Money taxableAmount, Money vatAmount)
    {
        TaxClass = taxClass;
        TaxableAmount = taxableAmount;
        VatAmount = vatAmount;
    }
}
