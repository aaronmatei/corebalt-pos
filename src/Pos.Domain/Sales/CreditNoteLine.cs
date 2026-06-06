using Pos.Domain.Catalog;
using Pos.SharedKernel;

namespace Pos.Domain.Sales;

/// <summary>
/// One returned line on a credit note. Quantity is the POSITIVE quantity returned; unit price + tax
/// class are carried from the original sale line. VAT is backed out of the inclusive total and frozen
/// (immutable fact) the same way a sale line does — the credit note is born complete.
/// </summary>
public sealed class CreditNoteLine : Entity
{
    public Guid ProductId { get; private set; }
    public string Description { get; private set; }
    public decimal Quantity { get; private set; }      // positive units returned
    public Money UnitPrice { get; private set; }        // VAT-inclusive, from the original sale
    public TaxClass TaxClass { get; private set; }
    public UnitOfMeasure UnitOfMeasure { get; private set; }
    public Money VatAmount { get; private set; }
    public Money TaxableAmount { get; private set; }

    public Money LineTotal => UnitPrice.Multiply(Quantity); // VAT-inclusive magnitude returned

    private CreditNoteLine()
    {
        Description = string.Empty;
        UnitPrice = Money.Zero();
        VatAmount = Money.Zero();
        TaxableAmount = Money.Zero();
    } // EF

    internal CreditNoteLine(Guid id, Guid productId, string description, decimal quantity, Money unitPrice,
        TaxClass taxClass, UnitOfMeasure unitOfMeasure) : base(id)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Return quantity must be positive.");
        ProductId = productId;
        Description = description;
        Quantity = quantity;
        // Clone — never reuse the original sale line's Money instance (EF owned types are tracked by
        // reference; sharing one across aggregates corrupts the change tracker).
        UnitPrice = new Money(unitPrice.Amount, unitPrice.Currency);
        TaxClass = taxClass;
        UnitOfMeasure = unitOfMeasure;

        var total = LineTotal;
        if (taxClass == TaxClass.StandardRated)
        {
            var rate = KraVat.StandardRatePercent;
            var vat = new Money(total.Amount * rate / (100m + rate), total.Currency); // total * 16/116
            VatAmount = vat;
            TaxableAmount = total.Subtract(vat);
        }
        else
        {
            VatAmount = Money.Zero(total.Currency);
            TaxableAmount = total;
        }
    }
}
