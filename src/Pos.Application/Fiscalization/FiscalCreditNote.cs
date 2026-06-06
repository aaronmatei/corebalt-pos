using System.Globalization;
using Pos.Domain.Sales;

namespace Pos.Application.Fiscalization;

/// <summary>
/// The credit note handed to the fiscalization provider — projected from the persisted CreditNote +
/// seller PIN. References the ORIGINAL receipt number and CUIN (the document being reversed).
/// </summary>
public sealed record FiscalCreditNote(
    Guid CreditNoteId,
    string SellerPin,
    string ReturnNumber,
    string OriginalReceiptNumber,
    string? OriginalCuin,
    string DateTimeEat,
    IReadOnlyList<FiscalLine> Lines,
    IReadOnlyList<FiscalVatLine> VatByClass,
    decimal TaxableTotal,
    decimal VatTotal,
    decimal GrandTotal,
    string Currency)
{
    public static FiscalCreditNote From(CreditNote note, string sellerPin)
    {
        var lines = note.Lines.Select(l => new FiscalLine(
            l.Description, l.Quantity, l.UnitPrice.Amount, l.TaxClass.ToString(),
            l.TaxableAmount.Amount, l.VatAmount.Amount)).ToList();

        var vat = note.Lines
            .GroupBy(l => l.TaxClass)
            .Select(g => new FiscalVatLine(g.Key.ToString(), g.Sum(x => x.TaxableAmount.Amount), g.Sum(x => x.VatAmount.Amount)))
            .ToList();

        return new FiscalCreditNote(
            note.Id, sellerPin,
            note.ReturnNumber ?? note.Id.ToString(),
            note.OriginalReceiptNumber,
            note.OriginalEtimsCuin,
            note.CreatedAtUtc.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            lines, vat,
            TaxableTotal: vat.Sum(v => v.Taxable),
            VatTotal: vat.Sum(v => v.Vat),
            GrandTotal: note.GrandTotal.Amount,
            Currency: note.Currency);
    }
}
