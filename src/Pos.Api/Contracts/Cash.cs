using Pos.Application.Cash;

namespace Pos.Api.Contracts;

public sealed record OpenSessionRequest(Guid RegisterId, decimal OpeningFloat);
public sealed record CashMovementRequest(Guid RegisterId, string Type, decimal Amount, string? Reason);
public sealed record CloseSessionRequest(decimal CountedCash, bool Acknowledged);

public sealed record SessionResponse(
    Guid Id, Guid RegisterId, string RegisterLabel, string Status,
    string OpenedBy, string OpenedAtEat, decimal OpeningFloat,
    string? ClosedBy, string? ClosedAtEat,
    decimal? CountedCash, decimal? ExpectedCash, decimal? Variance, string Currency);

/// <summary>Both the structured report (totals by tender, VAT, cash reconciliation) and the rendered
/// fixed-width text (what prints / what the back-office shows in a &lt;pre&gt;).</summary>
public sealed record ShiftReportResponse(ShiftReport Report, string Text);
