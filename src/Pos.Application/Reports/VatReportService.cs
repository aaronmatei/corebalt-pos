using System.Globalization;
using System.Text;
using Pos.Application.Sales;
using Pos.Domain.Catalog;

namespace Pos.Application.Reports;

/// <summary>
/// A KRA-filing VAT report: output tax owed for a date range, broken down by tax class from the
/// immutable per-line VAT that <see cref="Pos.Domain.Sales.Sale"/> froze at completion (never recomputed
/// here). Read-only projection over completed sales — same window always yields the same figures.
/// </summary>
public sealed class VatReportService
{
    private readonly ISaleRepository _sales;
    public VatReportService(ISaleRepository sales) => _sales = sales;

    /// <param name="fromUtc">inclusive</param><param name="toExclusiveUtc">exclusive</param>
    public async Task<VatReport> BuildAsync(Guid tenantId, Guid storeId,
        DateOnly fromDate, DateOnly toDate, DateTimeOffset fromUtc, DateTimeOffset toExclusiveUtc,
        CancellationToken ct = default)
    {
        var sales = await _sales.ListCompletedBetweenAsync(tenantId, storeId, fromUtc, toExclusiveUtc, ct);
        var currency = sales.Select(s => s.Currency).FirstOrDefault() ?? "KES";

        // Sum the frozen per-class VAT summary across every sale; track distinct sales per class.
        var agg = new Dictionary<TaxClass, (decimal Taxable, decimal Vat, HashSet<Guid> Sales)>();
        foreach (var sale in sales)
            foreach (var v in sale.VatSummary)
            {
                if (!agg.TryGetValue(v.TaxClass, out var cur))
                    cur = (0m, 0m, new HashSet<Guid>());
                cur.Taxable += v.TaxableAmount.Amount;
                cur.Vat += v.VatAmount.Amount;
                cur.Sales.Add(sale.Id);
                agg[v.TaxClass] = cur;
            }

        var lines = agg
            .OrderBy(kv => (int)kv.Key)
            .Select(kv => new VatReportLine(
                TaxClass: kv.Key.ToString(),
                // Effective rate from the data itself (16% standard, 0% zero-rated/exempt) — no hardcoded %.
                RatePercent: kv.Value.Taxable == 0m ? 0m : Math.Round(kv.Value.Vat / kv.Value.Taxable * 100m, 2),
                Taxable: kv.Value.Taxable,
                Vat: kv.Value.Vat,
                Gross: kv.Value.Taxable + kv.Value.Vat,
                SaleCount: kv.Value.Sales.Count))
            .ToList();

        return new VatReport(
            From: fromDate,
            To: toDate,
            Lines: lines,
            TotalTaxable: lines.Sum(l => l.Taxable),
            TotalVat: lines.Sum(l => l.Vat),
            TotalGross: lines.Sum(l => l.Gross),
            SaleCount: sales.Count,
            Currency: currency);
    }

    /// <summary>Render the report as a CSV (one row per tax class + a TOTAL row) for download / filing.</summary>
    public static string ToCsv(VatReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"VAT report,{r.From:yyyy-MM-dd},to,{r.To:yyyy-MM-dd},currency,{r.Currency}");
        sb.AppendLine("Tax class,Rate %,Taxable,VAT,Gross,Sales");
        foreach (var l in r.Lines)
            sb.AppendLine(string.Join(',', l.TaxClass, F(l.RatePercent), F(l.Taxable), F(l.Vat), F(l.Gross), l.SaleCount));
        sb.AppendLine(string.Join(',', "TOTAL", "", F(r.TotalTaxable), F(r.TotalVat), F(r.TotalGross), r.SaleCount));
        return sb.ToString();

        static string F(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

public sealed record VatReportLine(string TaxClass, decimal RatePercent, decimal Taxable, decimal Vat, decimal Gross, int SaleCount);

public sealed record VatReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<VatReportLine> Lines,
    decimal TotalTaxable,
    decimal TotalVat,
    decimal TotalGross,
    int SaleCount,
    string Currency);
