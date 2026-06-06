using System.Globalization;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;

namespace Pos.Application.Receipts;

/// <summary>
/// A DETERMINISTIC projection of a completed, persisted Sale + Store config — the single source the
/// renderers read. Every value comes from the stored sale (VAT/totals are NOT recomputed here) or
/// from static config, and there is no "now" anywhere, so two projections of the same sale are equal
/// and reprints are byte-identical.
/// </summary>
public sealed record ReceiptModel(
    ReceiptHeader Header,
    ReceiptMeta Meta,
    IReadOnlyList<ReceiptItem> Items,
    IReadOnlyList<ReceiptVatLine> Vat,
    ReceiptTotals Totals,
    IReadOnlyList<ReceiptTender> Tenders,
    decimal Change,
    string? BuyerPin,
    ReceiptFiscal Fiscal,
    IReadOnlyList<ReceiptLegend> Legend,
    string Currency)
{
    public static ReceiptModel From(Sale sale, StoreInfo store, ReceiptOptions options)
    {
        var header = new ReceiptHeader(store.LegalName, store.BranchName, store.BranchAddress,
            store.KraPin, store.VatNumber, store.Phone);

        var meta = new ReceiptMeta(
            ReceiptNo: sale.Id.ToString(),
            DateTimeEat: Eat(sale.CompletedAtUtc) ?? "",
            Cashier: Short(sale.CashierId),
            Register: Short(sale.RegisterId),
            Branch: store.BranchName);

        var items = sale.Lines.Select(l => new ReceiptItem(
            l.Description, QtyLine(l), l.LineTotal.Amount, options.TaxCode(l.TaxClass))).ToList();

        // VAT breakdown + totals come straight from the STORED summary — never recomputed.
        var vat = sale.VatSummary.Select(v => new ReceiptVatLine(
            options.TaxCode(v.TaxClass), ReceiptOptions.ClassLabel(v.TaxClass),
            v.TaxableAmount.Amount, v.VatAmount.Amount)).ToList();

        var totals = new ReceiptTotals(
            Subtotal: sale.VatSummary.Aggregate(0m, (s, v) => s + v.TaxableAmount.Amount),
            TotalVat: sale.VatSummary.Aggregate(0m, (s, v) => s + v.VatAmount.Amount),
            GrandTotal: sale.GrandTotal.Amount);

        var tenders = sale.Tenders.Where(t => t.IsConfirmed)
            .Select(t => new ReceiptTender(t.Type.ToString(), t.Amount.Amount, t.Reference)).ToList();

        var paid = tenders.Aggregate(0m, (s, t) => s + t.Amount);
        var change = paid > sale.GrandTotal.Amount ? paid - sale.GrandTotal.Amount : 0m;

        var transmitted = !string.IsNullOrWhiteSpace(sale.EtimsCuin);
        var fiscal = new ReceiptFiscal(
            transmitted, sale.EtimsCuin, sale.EtimsSignature, sale.EtimsQrUrl,
            Eat(sale.EtimsTransmittedAtUtc),
            transmitted ? $"eTIMS CU INV: {sale.EtimsCuin}" : "eTIMS: PENDING TRANSMISSION");

        var legend = sale.VatSummary
            .Select(v => new ReceiptLegend(options.TaxCode(v.TaxClass), ReceiptOptions.ClassLabel(v.TaxClass)))
            .ToList();

        return new ReceiptModel(header, meta, items, vat, totals, tenders, change,
            BuyerPin: null, fiscal, legend, store.Currency);
    }

    private static string Short(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private static string? Eat(DateTimeOffset? utc) =>
        utc?.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string QtyLine(SaleLine l)
    {
        var price = ReceiptFormat.Money(l.UnitPrice.Amount);
        return l.UnitOfMeasure == UnitOfMeasure.Kg
            ? $"{l.Quantity.ToString("0.000", CultureInfo.InvariantCulture)} kg @ {price}"
            : $"{l.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} @ {price}";
    }
}

public sealed record ReceiptHeader(string LegalName, string BranchName, string BranchAddress, string KraPin, string VatNumber, string Phone);
public sealed record ReceiptMeta(string ReceiptNo, string DateTimeEat, string Cashier, string Register, string Branch);
public sealed record ReceiptItem(string Description, string QtyLine, decimal LineTotal, string TaxCode);
public sealed record ReceiptVatLine(string TaxCode, string ClassLabel, decimal Taxable, decimal Vat);
public sealed record ReceiptTotals(decimal Subtotal, decimal TotalVat, decimal GrandTotal);
public sealed record ReceiptTender(string Type, decimal Amount, string? Reference);
public sealed record ReceiptFiscal(bool Transmitted, string? Cuin, string? Signature, string? QrUrl, string? TransmittedAtEat, string StatusText);
public sealed record ReceiptLegend(string Code, string Label);

public static class ReceiptFormat
{
    /// <summary>Money as 1,234.50 — thousands separator + 2dp, culture-invariant for byte-identical reprints.</summary>
    public static string Money(decimal amount) => amount.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
