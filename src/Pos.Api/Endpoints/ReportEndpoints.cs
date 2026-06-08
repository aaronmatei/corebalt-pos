using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Application.Abstractions;
using Pos.Application.Reports;

namespace Pos.Api.Endpoints;

/// <summary>
/// Management reports that aggregate immutable sale facts. Today: the KRA VAT/output-tax report for a
/// date range, as JSON or a downloadable CSV. Manager-gated (financial data).
/// </summary>
internal static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReports(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/reports").WithTags("Reports");

        // GET /reports/vat?from=2026-06-01&to=2026-06-30&format=csv
        // Dates are EAT (UTC+3) calendar days; `to` is INCLUSIVE. Defaults to the current month.
        g.MapGet("/vat", async (string? from, string? to, string? format,
            ICurrentContext ctx, VatReportService reports, CancellationToken ct) =>
        {
            var (fromDate, toDate, fromUtc, toExclusiveUtc) = EatRange(from, to);
            var report = await reports.BuildAsync(ctx.TenantId, ctx.StoreId, fromDate, toDate, fromUtc, toExclusiveUtc, ct);

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                var csv = VatReportService.ToCsv(report);
                return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv",
                    $"vat-report-{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.csv");
            }
            return Results.Ok(report);
        }).RequireAuthorization("Manager");

        return app;
    }

    /// <summary>UTC window for an EAT (UTC+3) [from, to] INCLUSIVE day range. Defaults to the current EAT month.</summary>
    internal static (DateOnly from, DateOnly to, DateTimeOffset fromUtc, DateTimeOffset toExclusiveUtc) EatRange(string? from, string? to)
    {
        var eat = TimeSpan.FromHours(3);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(eat).Date);
        var fromDate = DateOnly.TryParse(from, out var f) ? f : new DateOnly(today.Year, today.Month, 1);
        var toDate = DateOnly.TryParse(to, out var t) ? t : today;
        if (toDate < fromDate) toDate = fromDate;

        var startUtc = new DateTimeOffset(fromDate.Year, fromDate.Month, fromDate.Day, 0, 0, 0, eat).ToUniversalTime();
        var endExclusiveUtc = new DateTimeOffset(toDate.Year, toDate.Month, toDate.Day, 0, 0, 0, eat).AddDays(1).ToUniversalTime();
        return (fromDate, toDate, startUtc, endExclusiveUtc);
    }
}
