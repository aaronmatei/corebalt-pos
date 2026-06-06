using Pos.Domain.Catalog;
using Pos.SharedKernel;

namespace Pos.Domain.Sales;

public sealed class SaleLine : Entity
{
    public Guid ProductId { get; private set; }
    public string Description { get; private set; }
    public decimal Quantity { get; private set; }   // decimal: supports weighed goods (e.g. 0.450 kg)
    public Money UnitPrice { get; private set; }     // VAT-inclusive (Kenyan retail prices include VAT)
    public TaxClass TaxClass { get; private set; }
    public UnitOfMeasure UnitOfMeasure { get; private set; }

    // VAT is BACKED OUT of the inclusive total and STORED at completion (INVARIANT #3 — an immutable
    // fact, never recomputed at print time). Zero until FinalizeTax runs.
    public Money VatAmount { get; private set; }
    public Money TaxableAmount { get; private set; }

    public Money LineTotal => UnitPrice.Multiply(Quantity); // VAT-inclusive line total

    private SaleLine()
    {
        Description = string.Empty;
        UnitPrice = Money.Zero();
        VatAmount = Money.Zero();
        TaxableAmount = Money.Zero();
    } // EF

    internal SaleLine(Guid id, Guid productId, string description, decimal quantity, Money unitPrice,
        TaxClass taxClass, UnitOfMeasure unitOfMeasure) : base(id)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        ProductId = productId;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        TaxClass = taxClass;
        UnitOfMeasure = unitOfMeasure;
        VatAmount = Money.Zero(unitPrice.Currency);
        TaxableAmount = Money.Zero(unitPrice.Currency);
    }

    /// <summary>
    /// Back VAT out of the VAT-inclusive line total and freeze it. Called once, at completion.
    /// Standard-rated: VAT = total * 16/116, Taxable = total - VAT. Zero-rated/Exempt: VAT = 0.
    /// </summary>
    internal void FinalizeTax()
    {
        var total = LineTotal;
        if (TaxClass == TaxClass.StandardRated)
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
