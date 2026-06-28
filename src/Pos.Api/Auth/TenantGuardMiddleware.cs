using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Pos.Application.Abstractions;
using Pos.Application.Identity;

namespace Pos.Api.Auth;

/// <summary>
/// Hq mode defense-in-depth: once a request is authenticated, the principal's tenant (<c>tid</c>) MUST
/// match the subdomain it arrived on — a token/cookie minted for <c>acme</c> can never act on
/// <c>globex.pos.*</c>. (Back-office cookies are already host-only per subdomain, so this mainly hardens
/// the JWT surface and any misconfiguration.) Must run AFTER authentication and the dev-header bypass,
/// and BEFORE authorization. A no-op in StoreServer mode and for unauthenticated/unresolved requests.
/// </summary>
public sealed class TenantGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DeploymentOptions _deployment;

    public TenantGuardMiddleware(RequestDelegate next, DeploymentOptions deployment)
    {
        _next = next;
        _deployment = deployment;
    }

    public async Task Invoke(HttpContext ctx, ICurrentTenant tenant)
    {
        if (_deployment.IsHq
            && tenant.IsResolved
            && ctx.User?.Identity?.IsAuthenticated == true
            && Guid.TryParse(ctx.User.FindFirstValue(PosClaims.TenantId), out var principalTid)
            && principalTid != tenant.TenantId)
        {
            // Clear any cookie session and refuse — the credential belongs to a different tenant.
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(ctx);
    }
}
