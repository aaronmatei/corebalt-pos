using System.Globalization;
using Pos.Application.Receipts;
using Pos.Application.Sales;
using Pos.Domain.Cash;
using Pos.Domain.Sales;

namespace Pos.Application.Cash;

/// <summary>
/// Read-side projections over a session's (or a day's) immutable facts. Every figure is summed from
/// rows — sales tenders, VAT summaries, credit notes, cash movements — with NO mutable running counter,
/// so the same closed session always renders the same X/Z report.
/// </summary>
public sealed class CashOfficeReportService
{
    private readonly ISaleRepository _sales;
    private readonly ICreditNoteRepository _creditNotes;
    private readonly ICashMovementRepository _movements;
    private readonly ReceiptOptions _options;

    public CashOfficeReportService(ISaleRepository sales, ICreditNoteRepository creditNotes,
        ICashMovementRepository movements, ReceiptOptions options)
    {
        _sales = sales;
        _creditNotes = creditNotes;
        _movements = movements;
        _options = options;
    }

    /// <summary>Build the X (open) or Z (closed) report for a session from its facts.</summary>
    public async Task<ShiftReport> BuildAsync(RegisterSession s, CancellationToken ct = default)
    {
        var sales = await _sales.ListBySessionAsync(s.TenantId, s.StoreId, s.Id, ct);
        var notes = await _creditNotes.ListBySessionAsync(s.TenantId, s.StoreId, s.Id, ct);
        var movements = await _movements.ListBySessionAsync(s.TenantId, s.StoreId, s.Id, ct);
        var currency = s.OpeningFloat.Currency;

        var (gross, txns, items, tenders, vat, cashSalesNet) = SummariseSales(sales);

        var cashiers = sales.Select(x => x.CashierName).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct().OrderBy(n => n).ToList();
        if (cashiers.Count == 0 && !string.IsNullOrWhiteSpace(s.OpenedByName)) cashiers.Add(s.OpenedByName);

        var returns = notes.Where(n => !n.IsVoid).ToList();
        var voids = notes.Where(n => n.IsVoid).ToList();
        var cashRefunds = notes.Where(n => n.RefundMethod == RefundMethod.Cash).Sum(n => n.RefundAmount.Amount);

        var payIns = movements.Where(m => m.Type == CashMovementType.PayIn).Sum(m => m.Amount.Amount);
        var payOuts = movements.Where(m => m.Type == CashMovementType.PayOut).Sum(m => m.Amount.Amount);
        var drops = movements.Where(m => m.Type == CashMovementType.Drop).Sum(m => m.Amount.Amount);

        // Expected drawer cash = opening float + net cash sales − cash refunds + pay-ins − pay-outs − drops.
        var expected = s.OpeningFloat.Amount + cashSalesNet - cashRefunds + payIns - payOuts - drops;

        var cash = new CashReconciliation(s.OpeningFloat.Amount, cashSalesNet, cashRefunds, payIns, payOuts, drops,
            Expected: expected, Counted: s.CountedCash?.Amount, Variance: s.Variance?.Amount);

        return new ShiftReport(
            Kind: s.IsOpen ? "X" : "Z",
            SessionId: s.Id,
            RegisterId: s.RegisterId,
            RegisterLabel: string.IsNullOrWhiteSpace(s.RegisterLabel) ? "Till" : s.RegisterLabel,
            OpenedBy: s.OpenedByName,
            ClosedBy: s.ClosedByName,
            OpenedAtEat: Eat(s.OpenedAtUtc),
            ClosedAtEat: s.ClosedAtUtc is { } c ? Eat(c) : null,
            Cashiers: cashiers,
            GrossSales: gross,
            TransactionCount: txns,
            ItemCount: items,
            Tenders: tenders,
            Vat: vat,
            ReturnsCount: returns.Count,
            ReturnsAmount: returns.Sum(n => n.RefundAmount.Amount),
            VoidsCount: voids.Count,
            VoidsAmount: voids.Sum(n => n.RefundAmount.Amount),
            Cash: cash,
            Currency: currency);
    }

    /// <summary>The store's takings for a UTC day window: gross, by-tender and VAT across ALL sessions.</summary>
    public async Task<DaySummary> BuildDaySummaryAsync(Guid tenantId, Guid storeId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var sales = await _sales.ListCompletedBetweenAsync(tenantId, storeId, fromUtc, toUtc, ct);
        var notes = await _creditNotes.ListBetweenAsync(tenantId, storeId, fromUtc, toUtc, ct);
        var (gross, txns, items, tenders, vat, _) = SummariseSales(sales);
        var currency = sales.Select(x => x.Currency).FirstOrDefault() ?? "KES";

        return new DaySummary(
            FromUtc: fromUtc, ToUtc: toUtc, GrossSales: gross, TransactionCount: txns, ItemCount: items,
            Tenders: tenders, Vat: vat,
            ReturnsCount: notes.Count, ReturnsAmount: notes.Sum(n => n.RefundAmount.Amount), Currency: currency);
    }

    private (decimal gross, int txns, decimal items, List<ReportTenderTotal> tenders, List<ReportVatLine> vat, decimal cashSalesNet)
        SummariseSales(IReadOnlyList<Sale> sales)
    {
        var gross = 0m;
        var items = 0m;
        var cashSalesNet = 0m;
        var tender = new Dictionary<TenderType, (int Count, decimal Amount)>();
        var vat = new Dictionary<Domain.Catalog.TaxClass, (decimal Net, decimal Vat)>();

        foreach (var sale in sales)
        {
            gross += sale.GrandTotal.Amount;
            items += sale.Lines.Sum(l => l.Quantity);

            var confirmed = sale.Tenders.Where(t => t.IsConfirmed).ToList();
            foreach (var t in confirmed)
            {
                var cur = tender.TryGetValue(t.Type, out var x) ? x : (Count: 0, Amount: 0m);
                tender[t.Type] = (cur.Count + 1, cur.Amount + t.Amount.Amount);
            }
            var paid = confirmed.Sum(t => t.Amount.Amount);
            var change = Math.Max(0m, paid - sale.GrandTotal.Amount); // change is always given from cash
            var cashTendered = confirmed.Where(t => t.Type == TenderType.Cash).Sum(t => t.Amount.Amount);
            cashSalesNet += cashTendered - change;

            foreach (var v in sale.VatSummary)
            {
                var cur = vat.TryGetValue(v.TaxClass, out var x) ? x : (Net: 0m, Vat: 0m);
                vat[v.TaxClass] = (cur.Net + v.TaxableAmount.Amount, cur.Vat + v.VatAmount.Amount);
            }
        }

        var tenderTotals = tender.OrderBy(kv => (int)kv.Key)
            .Select(kv => new ReportTenderTotal(kv.Key.ToString(), kv.Value.Count, kv.Value.Amount)).ToList();
        var vatLines = vat.OrderBy(kv => (int)kv.Key)
            .Select(kv => new ReportVatLine(_options.TaxCode(kv.Key), ReceiptOptions.ClassLabel(kv.Key), kv.Value.Net, kv.Value.Vat)).ToList();

        return (gross, sales.Count, items, tenderTotals, vatLines, cashSalesNet);
    }

    private static string Eat(DateTimeOffset utc) =>
        utc.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}

public sealed record DaySummary(
    DateTimeOffset FromUtc, DateTimeOffset ToUtc,
    decimal GrossSales, int TransactionCount, decimal ItemCount,
    IReadOnlyList<ReportTenderTotal> Tenders, IReadOnlyList<ReportVatLine> Vat,
    int ReturnsCount, decimal ReturnsAmount, string Currency);
