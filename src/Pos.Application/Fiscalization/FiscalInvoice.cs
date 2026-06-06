using System.Globalization;
using Pos.Domain.Sales;

namespace Pos.Application.Fiscalization;

/// <summary>
/// The invoice handed to the fiscalization provider, projected from the PERSISTED, completed Sale +
/// the seller PIN (from Store config). Deterministic — VAT/totals are read from the stored sale.
/// </summary>
public sealed record FiscalInvoice(
    Guid SaleId,
    string SellerPin,
    string ReceiptNumber,
    string DateTimeEat,
    string? BuyerPin,
    IReadOnlyList<FiscalLine> Lines,
    IReadOnlyList<FiscalVatLine> VatByClass,
    decimal TaxableTotal,
    decimal VatTotal,
    decimal GrandTotal,
    string Currency)
{
    public static FiscalInvoice From(Sale sale, string sellerPin)
    {
        var lines = sale.Lines.Select(l => new FiscalLine(
            l.Description, l.Quantity, l.UnitPrice.Amount, l.TaxClass.ToString(),
            l.TaxableAmount.Amount, l.VatAmount.Amount)).ToList();

        var vat = sale.VatSummary.Select(v => new FiscalVatLine(
            v.TaxClass.ToString(), v.TaxableAmount.Amount, v.VatAmount.Amount)).ToList();

        return new FiscalInvoice(
            sale.Id,
            sellerPin,
            sale.ReceiptNumber ?? sale.Id.ToString(),
            sale.CompletedAtUtc?.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
            BuyerPin: null,
            lines,
            vat,
            TaxableTotal: vat.Sum(v => v.Taxable),
            VatTotal: vat.Sum(v => v.Vat),
            GrandTotal: sale.GrandTotal.Amount,
            Currency: sale.Currency);
    }
}

public sealed record FiscalLine(string Description, decimal Quantity, decimal UnitPrice, string TaxClass, decimal Taxable, decimal Vat);
public sealed record FiscalVatLine(string TaxClass, decimal Taxable, decimal Vat);
