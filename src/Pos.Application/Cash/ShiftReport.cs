using System.Globalization;
using Pos.Application.Receipts;
using Pos.Domain.Catalog;

namespace Pos.Application.Cash;

/// <summary>
/// A deterministic projection of ONE register session's facts (sales, returns, cash movements) into an
/// X or Z report. No mutable running counters — every figure is summed from immutable rows, so two reads
/// of the same closed session are identical. <see cref="Cash"/> carries the drawer reconciliation; on an
/// X report Counted/Variance are null (nothing is counted until close).
/// </summary>
public sealed record ShiftReport(
    string Kind,                       // "X" or "Z"
    Guid SessionId,
    Guid RegisterId,
    string RegisterLabel,
    string OpenedBy,
    string? ClosedBy,
    string OpenedAtEat,
    string? ClosedAtEat,
    IReadOnlyList<string> Cashiers,
    decimal GrossSales,
    int TransactionCount,
    decimal ItemCount,
    IReadOnlyList<ReportTenderTotal> Tenders,
    IReadOnlyList<ReportVatLine> Vat,
    IReadOnlyList<ReportCategoryLine> Categories,
    int ReturnsCount,
    decimal ReturnsAmount,
    int VoidsCount,
    decimal VoidsAmount,
    CashReconciliation Cash,
    string Currency);

public sealed record ReportTenderTotal(string Type, int Count, decimal Amount);
public sealed record ReportVatLine(string Code, string Label, decimal Net, decimal Vat);

/// <summary>Sales grouped by the product's CURRENT category (v1 joins to the live category; a product
/// recategorized after the sale moves with it). "Uncategorized" collects products with no category.</summary>
public sealed record ReportCategoryLine(string Name, decimal Gross, decimal Vat, decimal ItemCount);

public sealed record CashReconciliation(
    decimal OpeningFloat,
    decimal CashSales,   // net of change given
    decimal CashRefunds,
    decimal PayIns,
    decimal PayOuts,
    decimal Drops,
    decimal Expected,
    decimal? Counted,
    decimal? Variance);

/// <summary>Fixed-width monospace renderer for the X/Z report — back-office &lt;pre&gt; view and the body
/// of the ESC/POS print. Pure function of the projection (reuses the receipt layout helpers).</summary>
public static class ShiftReportTextRenderer
{
    public static string Render(ShiftReport r, int cols)
    {
        if (cols < 24) cols = 24;
        var sb = new System.Text.StringBuilder();
        void Line(string s) => sb.Append(s).Append('\n');
        string M(decimal d) => ReceiptText.Money(d);

        Line(ReceiptText.Rule('=', cols));
        Line(ReceiptText.Center($"{r.Kind}-REPORT", cols));
        Line(ReceiptText.Center(r.RegisterLabel, cols));
        Line(ReceiptText.Rule('=', cols));
        Line(ReceiptText.LeftRight("Opened:", r.OpenedAtEat, cols));
        Line($"  by {r.OpenedBy}");
        if (r.ClosedAtEat is not null)
        {
            Line(ReceiptText.LeftRight("Closed:", r.ClosedAtEat, cols));
            Line($"  by {r.ClosedBy}");
        }
        if (r.Cashiers.Count > 0) Line("Cashiers: " + string.Join(", ", r.Cashiers));
        Line(ReceiptText.Rule('-', cols));

        Line(ReceiptText.LeftRight("Transactions", r.TransactionCount.ToString(), cols));
        Line(ReceiptText.LeftRight("Items", r.ItemCount.ToString("0.###", CultureInfo.InvariantCulture), cols));
        Line(ReceiptText.LeftRight("GROSS SALES", $"{r.Currency} {M(r.GrossSales)}", cols));
        Line(ReceiptText.Rule('-', cols));

        Line("BY TENDER");
        foreach (var t in r.Tenders)
            Line(ReceiptText.LeftRight($"  {t.Type} (x{t.Count})", M(t.Amount), cols));
        Line(ReceiptText.Rule('-', cols));

        Line("VAT");
        foreach (var v in r.Vat)
            Line(ReceiptText.LeftRight($"  {v.Code} {v.Label}  net {M(v.Net)}", $"VAT {M(v.Vat)}", cols));
        Line(ReceiptText.Rule('-', cols));

        if (r.Categories.Count > 0)
        {
            Line("BY CATEGORY");
            foreach (var cat in r.Categories)
                Line(ReceiptText.LeftRight($"  {cat.Name}", M(cat.Gross), cols));
            Line(ReceiptText.Rule('-', cols));
        }

        Line(ReceiptText.LeftRight($"Returns (x{r.ReturnsCount})", M(r.ReturnsAmount), cols));
        Line(ReceiptText.LeftRight($"Voids (x{r.VoidsCount})", M(r.VoidsAmount), cols));
        Line(ReceiptText.Rule('-', cols));

        var c = r.Cash;
        Line("CASH DRAWER");
        Line(ReceiptText.LeftRight("  Opening float", M(c.OpeningFloat), cols));
        Line(ReceiptText.LeftRight("  Cash sales", M(c.CashSales), cols));
        Line(ReceiptText.LeftRight("  Cash refunds", "-" + M(c.CashRefunds), cols));
        Line(ReceiptText.LeftRight("  Pay-ins", M(c.PayIns), cols));
        Line(ReceiptText.LeftRight("  Pay-outs", "-" + M(c.PayOuts), cols));
        Line(ReceiptText.LeftRight("  Drops", "-" + M(c.Drops), cols));
        Line(ReceiptText.LeftRight("  EXPECTED", $"{r.Currency} {M(c.Expected)}", cols));
        if (c.Counted is { } counted)
        {
            Line(ReceiptText.LeftRight("  COUNTED", $"{r.Currency} {M(counted)}", cols));
            var variance = c.Variance ?? 0m;
            var label = variance == 0 ? "  VARIANCE" : variance > 0 ? "  VARIANCE (OVER)" : "  VARIANCE (SHORT)";
            Line(ReceiptText.LeftRight(label, M(variance), cols));
        }
        sb.Append(ReceiptText.Rule('=', cols));
        return sb.ToString();
    }

    /// <summary>The tax-class label shorthand used in the VAT section (mirrors the receipt's legend).</summary>
    public static string ClassLabel(TaxClass c) => ReceiptOptions.ClassLabel(c);
}
