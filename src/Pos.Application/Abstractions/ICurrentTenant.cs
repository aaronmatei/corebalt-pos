namespace Pos.Application.Abstractions;

/// <summary>
/// The tenant whose data the current request scope is allowed to touch, resolved BEFORE the user
/// authenticates:
/// <list type="bullet">
/// <item>StoreServer mode — the single configured tenant/store (always resolved).</item>
/// <item>Hq mode — the tenant addressed by the request subdomain (<c>acme.pos.* → "acme"</c>);
/// unresolved on the bare base host / admin surface.</item>
/// </list>
/// Used by login (which tenant's users to authenticate against), the tenant-name display, and as the
/// source for the EF global query-filter safety net. Once a user is authenticated, their principal's
/// <c>tid</c> claim is authoritative and (in Hq mode) MUST equal this resolved tenant — enforced by the
/// subdomain middleware so one tenant's cookie can never be replayed on another's subdomain.
/// </summary>
public interface ICurrentTenant
{
    bool IsResolved { get; }

    /// <summary>The resolved tenant id. <see cref="Guid.Empty"/> when <see cref="IsResolved"/> is false.</summary>
    Guid TenantId { get; }

    /// <summary>The store scope for this request. StoreServer: the configured store. Hq: the tenant's
    /// primary store from the registry (a tenant-wide back-office still filters per store at read time).</summary>
    Guid StoreId { get; }

    /// <summary>The subdomain slug in Hq mode (e.g. "acme"); null in StoreServer mode.</summary>
    string? Slug { get; }

    /// <summary>The tenant's human display name for the UI (e.g. "Acme Supermarket"); null if unknown.</summary>
    string? DisplayName { get; }
}
