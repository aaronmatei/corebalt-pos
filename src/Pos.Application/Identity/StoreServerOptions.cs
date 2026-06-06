namespace Pos.Application.Identity;

/// <summary>
/// The single tenant + store this store-server serves (per-branch, authoritative). Login resolves
/// users within this scope and the bootstrap manager is seeded here. Sourced from config.
/// </summary>
public sealed class StoreServerOptions
{
    public Guid TenantId { get; set; }
    public Guid StoreId { get; set; }
}
