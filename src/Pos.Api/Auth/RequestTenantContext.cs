using System.Security.Claims;
using Pos.Application.Abstractions;
using Pos.Application.Identity;

namespace Pos.Api.Auth;

/// <summary>
/// One scoped object that answers "which tenant is this request about?" for both:
/// <list type="bullet">
/// <item><see cref="ICurrentTenant"/> — the AMBIENT tenant resolved BEFORE login (subdomain in Hq mode,
/// config in StoreServer mode). Login uses it to know whose users to authenticate; the UI shows its name.</item>
/// <item><see cref="ITenantProvider"/> — the source for the EF global query-filter net. Once a user is
/// authenticated their principal's <c>tid</c> claim is authoritative (it's what writes use); before that
/// it falls back to the ambient tenant.</item>
/// </list>
/// In Hq mode the subdomain middleware calls <see cref="SetResolved"/> after looking the slug up; in
/// StoreServer mode the ambient tenant is always the configured one (no middleware needed).
/// </summary>
public sealed class RequestTenantContext : ICurrentTenant, ITenantProvider
{
    private readonly IHttpContextAccessor _http;
    private readonly DeploymentOptions _deployment;
    private readonly StoreServerOptions _server;

    private bool _resolved;
    private Guid _tenantId;
    private Guid _storeId;
    private string? _slug;
    private string? _displayName;

    public RequestTenantContext(IHttpContextAccessor http, DeploymentOptions deployment, StoreServerOptions server)
    {
        _http = http;
        _deployment = deployment;
        _server = server;
    }

    /// <summary>Called by the subdomain middleware (Hq mode) once a slug maps to an active tenant.</summary>
    public void SetResolved(Guid tenantId, Guid storeId, string slug, string displayName)
    {
        _resolved = true;
        _tenantId = tenantId;
        _storeId = storeId;
        _slug = slug;
        _displayName = displayName;
    }

    // ── ICurrentTenant (ambient, pre-auth) ──
    public bool IsResolved => _deployment.IsHq ? _resolved : _server.TenantId != Guid.Empty;
    public Guid TenantId => _deployment.IsHq ? _tenantId : _server.TenantId;
    public Guid StoreId => _deployment.IsHq ? _storeId : _server.StoreId;
    public string? Slug => _deployment.IsHq ? _slug : null;
    public string? DisplayName => _deployment.IsHq ? _displayName : null;

    // ── ITenantProvider (the EF query-filter source) ──
    // Filter ONLY when a concrete tenant comes from the REQUEST: the authenticated principal's tid (any
    // mode), or — before login — the Hq subdomain. Deliberately NOT the StoreServer config fallback: bare
    // scopes (background workers, design-time, cross-tenant admin/test provisioning) and StoreServer
    // anonymous routes (the M-Pesa callback, /setup) then run UNFILTERED, exactly as before this net
    // existed — their repository calls already pass an explicit TenantId, and on-prem is single-tenant.
    bool ITenantProvider.HasTenant => FilterTenant is not null;
    Guid ITenantProvider.TenantId => FilterTenant ?? Guid.Empty;

    private Guid? FilterTenant => PrincipalTenant ?? (_deployment.IsHq && _resolved ? _tenantId : (Guid?)null);

    private Guid? PrincipalTenant
    {
        get
        {
            var user = _http.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated == true
                && Guid.TryParse(user.FindFirstValue(PosClaims.TenantId), out var tid) && tid != Guid.Empty)
                return tid;
            return null;
        }
    }
}
