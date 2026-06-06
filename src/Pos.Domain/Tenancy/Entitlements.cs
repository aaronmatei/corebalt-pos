using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Tenancy;

public enum Edition { Retail = 0, Wholesale = 1, Supermarket = 2 }

/// <summary>Optional modules a tenant's licence unlocks.</summary>
[Flags]
public enum Feature
{
    None = 0,
    MultiBranch = 1 << 0,
    Promotions = 1 << 1,
    Loyalty = 1 << 2,
    Wholesale = 1 << 3,
    Procurement = 1 << 4,
}

/// <summary>
/// What a tenant's licence allows — edition, feature flags and limits, plus the licence key + expiry.
/// Lightweight now (offline, set at install/setup); a real activation service can refresh it later.
/// </summary>
public sealed class Entitlements : Entity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Edition Edition { get; private set; }
    public Feature Features { get; private set; }
    public int MaxTills { get; private set; }
    public int MaxBranches { get; private set; }
    public string? LicenseKey { get; private set; }
    public DateTimeOffset? ValidUntil { get; private set; }

    private Entitlements() { } // EF

    public static Entitlements Create(Guid tenantId, Edition edition, Feature features,
        int maxTills, int maxBranches, string? licenseKey, DateTimeOffset? validUntil) =>
        new()
        {
            Id = Uuid7.NewGuid(),
            TenantId = tenantId,
            Edition = edition,
            Features = features,
            MaxTills = maxTills < 1 ? 1 : maxTills,
            MaxBranches = maxBranches < 1 ? 1 : maxBranches,
            LicenseKey = string.IsNullOrWhiteSpace(licenseKey) ? null : licenseKey.Trim(),
            ValidUntil = validUntil,
        };

    /// <summary>The entitlements a verified Corebalt licence grants, stamped with the raw key.</summary>
    public static Entitlements FromLicense(Guid tenantId, Edition edition, Feature features,
        int maxTills, int maxBranches, string licenseKey, DateTimeOffset validUntil) =>
        Create(tenantId, edition, features, maxTills, maxBranches, licenseKey, validUntil);

    /// <summary>The baseline an unlicensed (or invalid/expired-licence) install runs on: Retail, no
    /// optional features, single till/branch. The gating authority when no valid key is present.</summary>
    public static Entitlements Unlicensed(Guid tenantId) =>
        Create(tenantId, Edition.Retail, Feature.None, 1, 1, null, null);

    /// <summary>Replace this tenant's licence in place (Settings "apply key") from a verified licence.</summary>
    public void ApplyLicense(Edition edition, Feature features, int maxTills, int maxBranches,
        string licenseKey, DateTimeOffset validUntil)
    {
        Edition = edition;
        Features = features;
        MaxTills = maxTills < 1 ? 1 : maxTills;
        MaxBranches = maxBranches < 1 ? 1 : maxBranches;
        LicenseKey = licenseKey;
        ValidUntil = validUntil;
    }

    public bool IsExpiredAsOf(DateTimeOffset now) => ValidUntil is { } until && now > until;

    public bool Has(Feature feature, DateTimeOffset now) =>
        !IsExpiredAsOf(now) && (Features & feature) == feature;
}
