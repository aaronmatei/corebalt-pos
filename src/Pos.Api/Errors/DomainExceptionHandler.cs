using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Auth;

namespace Pos.Api.Errors;

/// <summary>
/// Maps domain / argument / context exceptions to ProblemDetails. Keeps endpoints free of
/// try/catch — they just call CheckoutService / the repositories and let exceptions bubble.
/// </summary>
internal sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            MissingContextHeaderException => (StatusCodes.Status401Unauthorized, "Missing identity header"),
            ArgumentOutOfRangeException   => (StatusCodes.Status400BadRequest,   "Invalid argument"),
            ArgumentException             => (StatusCodes.Status400BadRequest,   "Invalid argument"),
            InvalidOperationException     => (StatusCodes.Status409Conflict,     "Domain rule violation"),
            _ => (0, string.Empty),
        };

        if (status == 0) return false; // let the default 500 pipeline handle it

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = ex.Message,
            Type = $"https://httpstatuses.io/{status}",
        };
        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(problem, cancellationToken: ct);
        return true;
    }
}
