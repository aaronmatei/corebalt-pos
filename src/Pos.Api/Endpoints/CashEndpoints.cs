using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Abstractions;
using Pos.Application.Cash;
using Pos.Application.Printing;
using Pos.Domain.Cash;

namespace Pos.Api.Endpoints;

/// <summary>
/// Cash management + close-of-day. A register SHIFT is the spine; X/Z reports are read-side projections.
/// Open/close: Cashier+. Pay-in/out/drop: Supervisor+ (anti-skimming). A large close variance needs a
/// Manager to acknowledge (enforced in CashOfficeService).
/// </summary>
internal static class CashEndpoints
{
    public static IEndpointRouteBuilder MapCash(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/sessions").WithTags("Cash");

        g.MapPost("/open", async (OpenSessionRequest req, CashOfficeService cash, CancellationToken ct) =>
        {
            var s = await cash.OpenAsync(req.RegisterId, req.OpeningFloat, ct);
            return Results.Created($"/api/v1/sessions/{s.Id}", Map(s));
        }).RequireAuthorization("CashierOrAbove");

        g.MapGet("/current", async (Guid registerId, CashOfficeService cash, CancellationToken ct) =>
        {
            var s = await cash.GetOpenAsync(registerId, ct);
            return s is null ? Results.NotFound() : Results.Ok(Map(s));
        }).RequireAuthorization("CashierOrAbove");

        // Drawer movement (Supervisor+). Attaches to the register's current open session.
        g.MapPost("/movements", async (CashMovementRequest req, CashOfficeService cash, CancellationToken ct) =>
        {
            if (!Enum.TryParse<CashMovementType>(req.Type, true, out var type))
                return Results.BadRequest(new { error = $"Unknown cash movement type '{req.Type}'." });
            var m = await cash.RecordMovementAsync(req.RegisterId, type, req.Amount, req.Reason, ct);
            return Results.Created($"/api/v1/sessions/movements/{m.Id}", new { m.Id, type = m.Type.ToString(), amount = m.Amount.Amount });
        }).RequireAuthorization("SupervisorOrAbove");

        // X report (open) or Z report (closed) for a session — viewable any time, mutates nothing.
        g.MapGet("/{id:guid}/report", async (Guid id, ICurrentContext ctx, IRegisterSessionRepository sessions,
            CashOfficeReportService reports, CancellationToken ct) =>
        {
            var session = await sessions.GetAsync(ctx.TenantId, ctx.StoreId, id, ct);
            if (session is null) return Results.NotFound();
            var report = await reports.BuildAsync(session, ct);
            return Results.Ok(new ShiftReportResponse(report, ShiftReportTextRenderer.Render(report, 48)));
        }).RequireAuthorization("CashierOrAbove");

        // Close (cash-up). Returns the Z report and prints it on the register's printer.
        g.MapPost("/{id:guid}/close", async (Guid id, CloseSessionRequest req, CashOfficeService cash,
            ReceiptOutputService output, CancellationToken ct) =>
        {
            var z = await cash.CloseAsync(id, req.CountedCash, req.Acknowledged, ct);
            await output.PrintShiftReportAsync(z.RegisterId, z, ct);
            return Results.Ok(new ShiftReportResponse(z, ShiftReportTextRenderer.Render(z, 48)));
        }).RequireAuthorization("CashierOrAbove");

        // Print the current X/Z report on the register's printer.
        g.MapPost("/{id:guid}/print", async (Guid id, ICurrentContext ctx, IRegisterSessionRepository sessions,
            CashOfficeReportService reports, ReceiptOutputService output, CancellationToken ct) =>
        {
            var session = await sessions.GetAsync(ctx.TenantId, ctx.StoreId, id, ct);
            if (session is null) return Results.NotFound();
            var report = await reports.BuildAsync(session, ct);
            await output.PrintShiftReportAsync(report.RegisterId, report, ct);
            return Results.Accepted();
        }).RequireAuthorization("CashierOrAbove");

        // Back-office review: sessions in a window (Supervisor+).
        g.MapGet("/", async (DateTimeOffset? from, DateTimeOffset? to, Guid? registerId,
            ICurrentContext ctx, IRegisterSessionRepository sessions, CancellationToken ct) =>
        {
            var fromUtc = from ?? DateTimeOffset.UtcNow.AddDays(-7);
            var toUtc = to ?? DateTimeOffset.UtcNow.AddDays(1);
            var list = await sessions.ListAsync(ctx.TenantId, ctx.StoreId, fromUtc, toUtc, registerId, ct);
            return Results.Ok(list.Select(Map));
        }).RequireAuthorization("SupervisorOrAbove");

        // Store/day sales summary across ALL sessions (sales by tender + VAT). `date` is an EAT day.
        app.MapGet("/sales-summary", async (string? date, ICurrentContext ctx, CashOfficeReportService reports, CancellationToken ct) =>
        {
            var (fromUtc, toUtc) = EatDay(date);
            var summary = await reports.BuildDaySummaryAsync(ctx.TenantId, ctx.StoreId, fromUtc, toUtc, ct);
            return Results.Ok(summary);
        }).RequireAuthorization("SupervisorOrAbove").WithTags("Cash");

        return app;
    }

    private static SessionResponse Map(RegisterSession s) => new(
        s.Id, s.RegisterId, s.RegisterLabel, s.Status.ToString(),
        s.OpenedByName, Eat(s.OpenedAtUtc), s.OpeningFloat.Amount,
        s.ClosedByName, s.ClosedAtUtc is { } c ? Eat(c) : null,
        s.CountedCash?.Amount, s.ExpectedCash?.Amount, s.Variance?.Amount, s.OpeningFloat.Currency);

    private static string Eat(DateTimeOffset utc) =>
        utc.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The UTC window for an EAT (UTC+3) calendar day (defaults to today EAT).</summary>
    private static (DateTimeOffset fromUtc, DateTimeOffset toUtc) EatDay(string? date)
    {
        var eat = TimeSpan.FromHours(3);
        var day = DateOnly.TryParse(date, out var d) ? d : DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(eat).Date);
        var start = new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, eat);
        return (start.ToUniversalTime(), start.AddDays(1).ToUniversalTime());
    }
}
