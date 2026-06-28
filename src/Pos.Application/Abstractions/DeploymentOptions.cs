namespace Pos.Application.Abstractions;

/// <summary>
/// How this <c>Pos.Api</c> process is deployed. The SAME binary runs in both modes (no rewrites,
/// honouring the store-authoritative invariant) — selected by the <c>Deployment:Mode</c> config key.
/// </summary>
public enum DeploymentMode
{
    /// <summary>On-prem, per-branch store server (the original model): tenant/store come from
    /// <c>StoreServer</c> config, the till sells offline-first, the first-run /setup wizard provisions
    /// the single tenant. Optionally pushes its outbox to the HQ tier.</summary>
    StoreServer,

    /// <summary>Cloud HQ / SaaS: multi-tenant, the active tenant is resolved from the request
    /// subdomain (e.g. <c>acme.pos.corebalt.co.ke</c> → slug "acme"). Hosts the back-office + reporting
    /// + the store→cloud sync ingest endpoint; tenants are admin-provisioned, not self-served via /setup.</summary>
    Hq,
}

/// <summary>
/// Process-wide deployment settings, bound from the <c>Deployment</c> config section. Singleton.
/// </summary>
public sealed class DeploymentOptions
{
    public DeploymentMode Mode { get; set; } = DeploymentMode.StoreServer;

    /// <summary>
    /// In <see cref="DeploymentMode.Hq"/>, the base host under which tenant subdomains live —
    /// e.g. <c>pos.corebalt.co.ke</c>. A request to <c>acme.pos.corebalt.co.ke</c> resolves the
    /// left-most label ("acme") as the tenant slug; a request to the bare base host (no slug) is the
    /// marketing/login-chooser/admin surface. Ignored in StoreServer mode.
    /// </summary>
    public string TenantBaseDomain { get; set; } = "";

    /// <summary>
    /// Optional: when this POS shares a reverse-proxy's on-demand-TLS <c>ask</c> with another co-hosted
    /// app (e.g. running behind the same Caddy), <c>/hq/tls-check</c> delegates hostnames that are NOT
    /// under <see cref="TenantBaseDomain"/> to this URL (the co-hosted app's own host check), appending
    /// <c>?domain={host}</c>. So one shared ask can authorize certs for both. Empty = refuse non-POS hosts.
    /// </summary>
    public string TlsCheckDelegateUrl { get; set; } = "";

    public bool IsHq => Mode == DeploymentMode.Hq;
}
