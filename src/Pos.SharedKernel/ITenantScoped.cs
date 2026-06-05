namespace Pos.SharedKernel;

/// <summary>
/// INVARIANT #4 — every record carries its tenant. Even though the first on-prem
/// customer is a single tenant, this is here from row one so the SaaS/multi-tenant
/// cloud tier is an addition later, never a migration.
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; }
}
