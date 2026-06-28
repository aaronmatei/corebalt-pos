using Pos.Application.Abstractions;
using Pos.Application.Tenancy;
using Pos.Domain.Tenancy;

namespace Pos.Api.Endpoints;

/// <summary>
/// The on-demand-TLS <c>ask</c> endpoint for a (possibly shared) Caddy: <c>GET /hq/tls-check?domain={sni}</c>.
/// Returns 200 only for the apex and ACTIVE tenant subdomains under <c>TenantBaseDomain</c>, so Caddy mints
/// certs on demand for real tenants and refuses random subdomains. Hostnames outside the POS base domain are
/// delegated to a co-hosted app's checker (<c>Deployment:TlsCheckDelegateUrl</c>) so one shared ask serves
/// both apps. Mapped only in Hq mode; anonymous (Caddy calls it server-to-server).
/// </summary>
internal static class TlsCheckEndpoints
{
    public static IEndpointRouteBuilder MapTlsCheck(this IEndpointRouteBuilder app)
    {
        app.MapGet("/hq/tls-check", async (HttpContext http, ITenantRepository tenants, DeploymentOptions dep,
            IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            var host = (http.Request.Query["domain"].FirstOrDefault() ?? "").Trim().TrimEnd('.').ToLowerInvariant();
            if (host.Length == 0) return Results.NotFound();

            var baseDomain = (dep.TenantBaseDomain ?? "").Trim().TrimEnd('.').ToLowerInvariant();
            if (baseDomain.Length > 0 && (host == baseDomain || host.EndsWith("." + baseDomain, StringComparison.Ordinal)))
            {
                if (host == baseDomain) return Results.Ok(); // apex (landing / admin surface)
                var label = host[..^(baseDomain.Length + 1)];
                string slug;
                try { slug = Tenant.NormalizeSlug(label); }
                catch (ArgumentException) { return Results.NotFound(); } // malformed/reserved → never issue
                var tenant = await tenants.GetBySlugAsync(slug, ct);
                return tenant is { IsActive: true } ? Results.Ok() : Results.NotFound();
            }

            // Not a POS host → delegate to the co-hosted app's checker (shared ask), if configured.
            if (string.IsNullOrWhiteSpace(dep.TlsCheckDelegateUrl)) return Results.NotFound();
            try
            {
                var client = httpFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var sep = dep.TlsCheckDelegateUrl.Contains('?') ? '&' : '?';
                using var resp = await client.GetAsync(
                    $"{dep.TlsCheckDelegateUrl}{sep}domain={Uri.EscapeDataString(host)}", ct);
                return resp.IsSuccessStatusCode ? Results.Ok() : Results.NotFound();
            }
            catch { return Results.NotFound(); } // delegate unreachable → refuse (never issue for unknown hosts)
        }).AllowAnonymous();

        return app;
    }
}
