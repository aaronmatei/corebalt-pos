namespace Pos.Application.Abstractions;

/// <summary>
/// The narrow seam the EF global query filter reads to scope every <c>ITenantScoped</c> read to the
/// current tenant — a defense-in-depth net under the repositories' explicit <c>WHERE TenantId = …</c>.
/// Pure (no DB / no async) so the <c>DbContext</c> can depend on it without a cycle.
/// <para>
/// When <see cref="HasTenant"/> is false the filter is disabled (it short-circuits to "match all") —
/// for contexts with no single tenant: design-time/migrations, the persistence samples, and the Hq
/// store→cloud sync ingester (which writes many tenants' rows and opts out via <c>IgnoreQueryFilters</c>).
/// </para>
/// </summary>
public interface ITenantProvider
{
    bool HasTenant { get; }
    Guid TenantId { get; }
}

/// <summary>Default provider for hosts with no request context (samples, design-time): never filters.</summary>
public sealed class NullTenantProvider : ITenantProvider
{
    public bool HasTenant => false;
    public Guid TenantId => Guid.Empty;
}
