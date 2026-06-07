using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Tenancy;

/// <summary>
/// Per-install operational settings the operator can edit in back-office Settings (schedule + retention
/// stay host config). Today it holds the OFF-MACHINE backup copy location (external drive / network
/// share) — the critical "don't keep the only copy on the same box" target.
/// </summary>
public sealed class OpsSettings : Entity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string? SecondBackupLocation { get; private set; }

    private OpsSettings() { } // EF

    public static OpsSettings Create(Guid tenantId) => new() { Id = Uuid7.NewGuid(), TenantId = tenantId };

    public void SetSecondBackupLocation(string? path) =>
        SecondBackupLocation = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
}
