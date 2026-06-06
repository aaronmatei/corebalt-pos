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
    string Currency,
    string DocumentTitle = "",       // e.g. "CREDIT NOTE / REFUND"; empty for a normal sale
    string? AgainstReceiptNo = null) // original receipt referenced by a credit note
{
    public static ReceiptModel From(Sale sale, StoreInfo store, ReceiptOptions options)
    {
        var header = new ReceiptHeader(store.LegalName, store.BranchName, store.BranchAddress,
            store.KraPin, store.VatNumber, store.Phone);

        var meta = new ReceiptMeta(
            ReceiptNo: sale.ReceiptNumber ?? sale.Id.ToString(), // human number; falls back to id pre-completion
            Ref: sale.Id.ToString(),                              // internal UUIDv7 — kept for support lookups
            DateTimeEat: Eat(sale.CompletedAtUtc) ?? "",
            // Real cashier name + staff code captured at sale time; fall back to the short id if absent
            // (e.g. legacy sales rung before authentication existed).
            Cashier: string.IsNullOrWhiteSpace(sale.CashierName)
                ? Short(sale.CashierId)
                : $"{sale.CashierName} ({sale.CashierStaffCode})",
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

        var fiscalized = sale.EtimsCuin is not null;
        var fiscal = new ReceiptFiscal(
            Status: sale.FiscalStatus.ToString(),
            Fiscalized: fiscalized,
            Cuin: sale.EtimsCuin,
            QrData: sale.EtimsQrUrl,
            SignedAtEat: Eat(sale.EtimsSignedAtUtc),
            SyncedAtEat: Eat(sale.EtimsTransmittedAtUtc),
            StatusText: fiscalized
                ? $"eTIMS CU INV: {sale.EtimsCuin}"
                : sale.FiscalStatus == FiscalStatus.NotRequired ? "NON-FISCAL / TRAINING" : "eTIMS: NOT FISCALIZED");

        var legend = sale.VatSummary
            .Select(v => new ReceiptLegend(options.TaxCode(v.TaxClass), ReceiptOptions.ClassLabel(v.TaxClass)))
            .ToList();

        return new ReceiptModel(header, meta, items, vat, totals, tenders, change,
            BuyerPin: null, fiscal, legend, store.Currency);
    }

    /// <summary>
    /// Projects a credit note (return/void) into the SAME model with a "CREDIT NOTE / REFUND" header,
    /// NEGATIVE quantities/amounts, the original receipt referenced, the refund tender, and the
    /// credit-note fiscal block. Deterministic, like the sale projection.
    /// </summary>
    public static ReceiptModel FromCreditNote(CreditNote note, StoreInfo store, ReceiptOptions options)
    {
        var header = new ReceiptHeader(store.LegalName, store.BranchName, store.BranchAddress,
            store.KraPin, store.VatNumber, store.Phone);

        var meta = new ReceiptMeta(
            ReceiptNo: note.ReturnNumber ?? note.Id.ToString(),
            Ref: note.Id.ToString(),
            DateTimeEat: Eat(note.CreatedAtUtc) ?? "",
            Cashier: string.IsNullOrWhiteSpace(note.AuthorizedByName)
                ? Short(note.AuthorizedBy)
                : $"{note.AuthorizedByName} ({note.AuthorizedByStaffCode})",
            Register: "—",
            Branch: store.BranchName);

        var items = note.Lines
            .Select(l => new ReceiptItem(l.Description, NegQty(l), -l.LineTotal.Amount, options.TaxCode(l.TaxClass)))
            .ToList();

        var vat = note.Lines.GroupBy(l => l.TaxClass).OrderBy(g => g.Key)
            .Select(g => new ReceiptVatLine(options.TaxCode(g.Key), ReceiptOptions.ClassLabel(g.Key),
                -g.Sum(x => x.TaxableAmount.Amount), -g.Sum(x => x.VatAmount.Amount)))
            .ToList();

        var totals = new ReceiptTotals(
            Subtotal: -note.Lines.Sum(l => l.TaxableAmount.Amount),
            TotalVat: -note.Lines.Sum(l => l.VatAmount.Amount),
            GrandTotal: -note.GrandTotal.Amount);

        var refundLabel = note.RefundStatus == RefundStatus.PendingManual
            ? $"Refund {note.RefundMethod} (MANUAL PENDING)"
            : $"Refund {note.RefundMethod}";
        var tenders = new List<ReceiptTender> { new(refundLabel, -note.RefundAmount.Amount, null) };

        var fiscalized = note.EtimsCuin is not null;
        var fiscal = new ReceiptFiscal(
            Status: note.FiscalStatus.ToString(),
            Fiscalized: fiscalized,
            Cuin: note.EtimsCuin,
            QrData: note.EtimsQrUrl,
            SignedAtEat: Eat(note.EtimsSignedAtUtc),
            SyncedAtEat: null,
            StatusText: fiscalized
                ? $"eTIMS CREDIT NOTE: {note.EtimsCuin}"
                : note.FiscalStatus == FiscalStatus.NotRequired ? "NON-FISCAL / TRAINING" : "eTIMS: NOT FISCALIZED",
            OriginalCuin: note.OriginalEtimsCuin);

        var legend = vat.Select(v => new ReceiptLegend(v.TaxCode, v.ClassLabel)).ToList();

        return new ReceiptModel(header, meta, items, vat, totals, tenders, Change: 0m,
            BuyerPin: null, fiscal, legend, store.Currency,
            DocumentTitle: "CREDIT NOTE / REFUND", AgainstReceiptNo: note.OriginalReceiptNumber);
    }

    private static string NegQty(CreditNoteLine l)
    {
        var price = ReceiptFormat.Money(l.UnitPrice.Amount);
        return l.UnitOfMeasure == UnitOfMeasure.Kg
            ? $"-{l.Quantity.ToString("0.000", CultureInfo.InvariantCulture)} kg @ {price}"
            : $"-{l.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} @ {price}";
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
public sealed record ReceiptMeta(string ReceiptNo, string Ref, string DateTimeEat, string Cashier, string Register, string Branch);
public sealed record ReceiptItem(string Description, string QtyLine, decimal LineTotal, string TaxCode);
public sealed record ReceiptVatLine(string TaxCode, string ClassLabel, decimal Taxable, decimal Vat);
public sealed record ReceiptTotals(decimal Subtotal, decimal TotalVat, decimal GrandTotal);
public sealed record ReceiptTender(string Type, decimal Amount, string? Reference);
public sealed record ReceiptFiscal(string Status, bool Fiscalized, string? Cuin, string? QrData, string? SignedAtEat, string? SyncedAtEat, string StatusText, string? OriginalCuin = null);
public sealed record ReceiptLegend(string Code, string Label);

public static class ReceiptFormat
{
    /// <summary>Money as 1,234.50 — thousands separator + 2dp, culture-invariant for byte-identical reprints.</summary>
    public static string Money(decimal amount) => amount.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
