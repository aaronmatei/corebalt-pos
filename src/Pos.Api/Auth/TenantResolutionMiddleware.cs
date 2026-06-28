using Pos.Application.Abstractions;
using Pos.Application.Tenancy;
using Pos.Domain.Tenancy;

namespace Pos.Api.Auth;

/// <summary>
/// Hq (cloud) mode only: resolves the request subdomain to a tenant BEFORE authentication, so login knows
/// whose users to check and the back-office renders the right tenant. <c>acme.pos.corebalt.co.ke</c> →
/// slug "acme" → the <c>tenants</c> registry row → <see cref="RequestTenantContext.SetResolved"/>.
/// <list type="bullet">
/// <item>The bare base host (<c>pos.corebalt.co.ke</c>, or a host outside the base domain) leaves the
/// tenant unresolved — the marketing/login-chooser/admin surface.</item>
/// <item>An unknown or deactivated slug short-circuits with 404 (no tenant data is ever touched).</item>
/// </list>
/// A no-op in StoreServer mode (the configured tenant is always ambient) and for <c>/healthz</c>.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DeploymentOptions _deployment;
    private readonly string _baseDomain;

    public TenantResolutionMiddleware(RequestDelegate next, DeploymentOptions deployment)
    {
        _next = next;
        _deployment = deployment;
        _baseDomain = (deployment.TenantBaseDomain ?? "").Trim().TrimEnd('.').ToLowerInvariant();
    }

    public async Task Invoke(HttpContext ctx, RequestTenantContext tenant, ITenantRepository tenants)
    {
        if (!_deployment.IsHq || ctx.Request.Path.StartsWithSegments("/healthz"))
        {
            await _next(ctx);
            return;
        }

        var slug = ExtractSlug(ctx.Request.Host.Host);
        if (slug is null)
        {
            // Bare base host / outside the base domain → apex surface (no tenant). Tenant-scoped pages
            // handle the absence themselves (e.g. the back-office redirects to login).
            await _next(ctx);
            return;
        }

        Tenant? row;
        try
        {
            var normalized = Tenant.NormalizeSlug(slug);
            row = await tenants.GetBySlugAsync(normalized, ctx.RequestAborted);
        }
        catch (ArgumentException) { row = null; } // malformed/reserved slug = not a tenant

        if (row is null || !row.IsActive)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.Response.WriteAsync(row is null ? "Unknown store." : "This store is not active.", ctx.RequestAborted);
            return;
        }

        tenant.SetResolved(row.Id, row.PrimaryStoreId, row.Slug, row.DisplayName);
        await _next(ctx);
    }

    /// <summary>The left-most label when the host is <c>{slug}.{baseDomain}</c>; null for the bare base
    /// host or a host outside the base domain. Returns the raw label (the registry lookup normalizes it).</summary>
    private string? ExtractSlug(string host)
    {
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(_baseDomain) || host == _baseDomain || host == "www." + _baseDomain)
            return null;
        var suffix = "." + _baseDomain;
        if (!host.EndsWith(suffix, StringComparison.Ordinal)) return null;
        var label = host[..^suffix.Length];
        return string.IsNullOrEmpty(label) ? null : label;
    }
}
