using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Pos.Api.Errors;

/// <summary>
/// Maps domain / argument / context exceptions to ProblemDetails. Keeps endpoints free of
/// try/catch — they just call CheckoutService / the repositories and let exceptions bubble.
/// </summary>
internal sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title, detail) = ex switch
        {
            ArgumentOutOfRangeException   => (StatusCodes.Status400BadRequest,   "Invalid argument", ex.Message),
            ArgumentException             => (StatusCodes.Status400BadRequest,   "Invalid argument", ex.Message),
            InvalidOperationException     => (StatusCodes.Status409Conflict,     "Domain rule violation", ex.Message),
            // Backstop for the unique indexes (e.g. SKU/barcode) when the app-level check loses a race:
            // Postgres unique_violation (SQLSTATE 23505) → a clean 409 instead of a raw 500.
            DbUpdateException { InnerException: DbException { SqlState: "23505" } }
                => (StatusCodes.Status409Conflict, "Duplicate value", "A unique value (such as SKU or barcode) is already in use."),
            _ => (0, string.Empty, string.Empty),
        };

        if (status == 0) return false; // let the default 500 pipeline handle it

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
        };
        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(problem, cancellationToken: ct);
        return true;
    }
}
