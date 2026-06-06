using Pos.Application.Identity;
using Pos.Application.Tenancy;

namespace Pos.Api.BackOffice;

/// <summary>
/// Routes a fresh, un-provisioned install to the first-run wizard: back-office PAGE navigations are
/// redirected to /setup until a MerchantProfile exists for this store-server's tenant, and away from
/// /setup once it does. API routes, back-office form posts, static assets, health and the M-Pesa
/// callback pass straight through.
/// </summary>
public sealed class SetupRedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly StoreServerOptions _server;

    public SetupRedirectMiddleware(RequestDelegate next, StoreServerOptions server)
    {
        _next = next;
        _server = server;
    }

    public async Task Invoke(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "/";
        if (_server.TenantId == Guid.Empty
            || path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/backoffice", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/healthz", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/mpesa", StringComparison.OrdinalIgnoreCase)
            || path.Contains('.')) // static assets (app.css, favicon.ico, ...)
        {
            await _next(ctx);
            return;
        }

        var setup = ctx.RequestServices.GetRequiredService<SetupService>();
        var complete = await setup.IsCompleteAsync(_server.TenantId);

        if (!complete && !path.Equals("/setup", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Redirect("/setup");
            return;
        }
        if (complete && path.Equals("/setup", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Redirect("/login");
            return;
        }

        await _next(ctx);
    }
}
