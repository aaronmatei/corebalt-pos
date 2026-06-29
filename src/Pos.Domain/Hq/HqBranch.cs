using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Hq;

/// <summary>
/// HQ/cloud registry of a tenant's branches (M3) — populated as each store self-registers on sync (its
/// StoreId + branch name). Lets a branch list the OTHER branches to pick a transfer destination. One row
/// per (tenant, store); surrogate id with a unique (tenant, store) index.
/// </summary>
public sealed class HqBranch : Entity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset LastSeenAtUtc { get; private set; }

    private HqBranch() { } // EF

    public static HqBranch Create(Guid tenantId, Guid storeId, string name, DateTimeOffset now) => new()
    {
        Id = Uuid7.NewGuid(),
        TenantId = tenantId,
        StoreId = storeId,
        Name = string.IsNullOrWhiteSpace(name) ? "Branch" : name.Trim(),
        LastSeenAtUtc = now,
    };

    public void Seen(string name, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        LastSeenAtUtc = now;
    }
}
