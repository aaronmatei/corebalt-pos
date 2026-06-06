using FluentAssertions;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Domain.Tests.Sales;

/// <summary>
/// VAT is computed and STORED at completion (immutable fact), backed out of VAT-inclusive prices.
/// </summary>
public sealed class SaleVatTests
{
    private static Sale NewSale() =>
        Sale.Start(Uuid7.NewGuid(), Uuid7.NewGuid(), Uuid7.NewGuid(), Uuid7.NewGuid());

    [Fact]
    public void Standard_rated_line_backs_16pct_vat_out_of_the_inclusive_total()
    {
        var sale = NewSale();
        sale.AddLine(Uuid7.NewGuid(), "Oil", 1, new Money(116m), TaxClass.StandardRated);
        sale.AddTender(TenderType.Cash, new Money(116m));
        sale.Complete();

        var line = sale.Lines.Single();
        line.VatAmount.Amount.Should().Be(16.00m);     // 116 * 16/116
        line.TaxableAmount.Amount.Should().Be(100.00m);
        (line.TaxableAmount.Amount + line.VatAmount.Amount).Should().Be(line.LineTotal.Amount);
    }

    [Fact]
    public void Zero_rated_and_exempt_lines_carry_no_vat()
    {
        var sale = NewSale();
        sale.AddLine(Uuid7.NewGuid(), "Bread", 1, new Money(110m), TaxClass.ZeroRated);
        sale.AddLine(Uuid7.NewGuid(), "Service", 1, new Money(200m), TaxClass.Exempt);
        sale.AddTender(TenderType.Cash, new Money(310m));
        sale.Complete();

        sale.Lines.Should().OnlyContain(l => l.VatAmount.Amount == 0m);
        sale.Lines.First().TaxableAmount.Amount.Should().Be(110m);
    }

    [Fact]
    public void Vat_summary_groups_by_class_and_grand_total_is_frozen()
    {
        var sale = NewSale();
        sale.AddLine(Uuid7.NewGuid(), "Oil", 1, new Money(116m), TaxClass.StandardRated);   // vat 16, taxable 100
        sale.AddLine(Uuid7.NewGuid(), "Beef", 1.250m, new Money(200m), TaxClass.StandardRated, UnitOfMeasure.Kg); // 250 incl; vat 34.48
        sale.AddLine(Uuid7.NewGuid(), "Bread", 1, new Money(110m), TaxClass.ZeroRated);      // vat 0, taxable 110
        sale.AddTender(TenderType.Cash, new Money(476m));
        sale.Complete();

        sale.GrandTotal.Amount.Should().Be(476m);

        var std = sale.VatSummary.Single(v => v.TaxClass == TaxClass.StandardRated);
        std.TaxableAmount.Amount.Should().Be(315.52m);
        std.VatAmount.Amount.Should().Be(50.48m);

        var zero = sale.VatSummary.Single(v => v.TaxClass == TaxClass.ZeroRated);
        zero.TaxableAmount.Amount.Should().Be(110m);
        zero.VatAmount.Amount.Should().Be(0m);

        // Summary reconciles with the per-line stored figures and the grand total.
        var summedTaxable = sale.VatSummary.Sum(v => v.TaxableAmount.Amount);
        var summedVat = sale.VatSummary.Sum(v => v.VatAmount.Amount);
        (summedTaxable + summedVat).Should().Be(sale.GrandTotal.Amount);
    }

    [Fact]
    public void Vat_is_not_stored_until_completion()
    {
        var sale = NewSale();
        sale.AddLine(Uuid7.NewGuid(), "Oil", 1, new Money(116m), TaxClass.StandardRated);

        sale.Lines.Single().VatAmount.Amount.Should().Be(0m, "VAT is frozen at completion, not while the cart is open");
        sale.VatSummary.Should().BeEmpty();
        sale.GrandTotal.Amount.Should().Be(0m);
    }
}
