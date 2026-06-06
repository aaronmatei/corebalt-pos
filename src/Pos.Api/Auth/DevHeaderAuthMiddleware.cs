using System.Security.Claims;
using Pos.Application.Identity;
using Pos.Domain.Identity;

namespace Pos.Api.Auth;

/// <summary>
/// DEV/TEST ONLY bypass (Auth:AllowDevHeaders=true, off by default, never in Production). When no JWT
/// authenticated the request, but trusted X-Tenant-Id/X-Store-Id/X-User-Id headers are present, it
/// synthesizes a Manager principal carrying the same claims a real token would — so the existing
/// header-driven tests and local curling keep working through the JWT authorization pipeline. Must run
/// AFTER UseAuthentication and BEFORE UseAuthorization.
/// </summary>
public sealed class DevHeaderAuthMiddleware
{
    public const string TenantHeader = "X-Tenant-Id";
    public const string StoreHeader = "X-Store-Id";
    public const string UserHeader = "X-User-Id";

    private readonly RequestDelegate _next;
    private readonly bool _enabled;

    public DevHeaderAuthMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _enabled = config.GetValue<bool>("Auth:AllowDevHeaders");
    }

    public Task Invoke(HttpContext ctx)
    {
        if (_enabled
            && ctx.User?.Identity?.IsAuthenticated != true
            && TryGuid(ctx, TenantHeader, out var tenant)
            && TryGuid(ctx, StoreHeader, out var store)
            && TryGuid(ctx, UserHeader, out var user))
        {
            var identity = new ClaimsIdentity(authenticationType: "DevHeaders", nameType: PosClaims.Name, roleType: PosClaims.Role);
            identity.AddClaim(new Claim(PosClaims.UserId, user.ToString()));
            identity.AddClaim(new Claim(PosClaims.TenantId, tenant.ToString()));
            identity.AddClaim(new Claim(PosClaims.StoreId, store.ToString()));
            identity.AddClaim(new Claim(PosClaims.Name, "Dev User"));
            identity.AddClaim(new Claim(PosClaims.StaffCode, "DEV"));
            identity.AddClaim(new Claim(PosClaims.Role, nameof(UserRole.Manager))); // dev gets full access
            ctx.User = new ClaimsPrincipal(identity);
        }

        return _next(ctx);
    }

    private static bool TryGuid(HttpContext ctx, string header, out Guid value)
    {
        value = Guid.Empty;
        return ctx.Request.Headers.TryGetValue(header, out var v) && v.Count > 0
            && Guid.TryParse(v[0], out value) && value != Guid.Empty;
    }
}
