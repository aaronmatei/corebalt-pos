namespace Pos.Application.Integration;

/// <summary>
/// Host-level config for forwarding completed sales to the Corebalt ERP (bound from the "CorebaltErp"
/// section). The ERP is the HQ in this deployment; the sale webhook authenticates with a shared
/// service token. Disabled by default — when off, the sync worker isn't registered.
///
/// <para>v1 maps this whole store server to ONE Corebalt tenant (<see cref="TenantSlug"/>). If a single
/// install ever serves multiple Corebalt tenants, move slug+token to a per-tenant setting.</para>
/// </summary>
public sealed class CorebaltErpOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;       // e.g. https://api-erp.corebalt.co.ke
    public string TenantSlug { get; set; } = string.Empty;    // the Corebalt company slug
    public string ServiceToken { get; set; } = string.Empty;  // matches POS_SERVICE_TOKEN on the ERP
    public int IntervalSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 100;
    public int TimeoutSeconds { get; set; } = 15;
}
