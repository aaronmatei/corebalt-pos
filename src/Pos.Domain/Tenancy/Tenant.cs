using System.Text;
using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Tenancy;

/// <summary>
/// HQ/cloud registry entry mapping a subdomain slug to a tenant: a request to
/// <c>acme.pos.corebalt.co.ke</c> resolves slug "acme" → this row → its <see cref="Entity.Id"/>, which
/// IS the <c>TenantId</c> carried by every tenant-scoped fact for that retailer.
/// <para>
/// Cloud-only master data, admin-provisioned (not self-served via the on-prem /setup wizard). Deliberately
/// NOT <see cref="ITenantScoped"/>: the subdomain middleware looks a tenant up BEFORE any tenant is known,
/// so this table must never be hidden by the tenant query filter.
/// </para>
/// </summary>
public sealed class Tenant : AggregateRoot
{
    public string Slug { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    /// <summary>The tenant's primary StoreId — the default store scope for cloud back-office requests and
    /// the first manager seeded at provision time. Per-store data still arrives from sync with its own StoreId.</summary>
    public Guid PrimaryStoreId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>SHA-256 (hex) of this tenant's store→cloud SYNC token. The on-prem store servers present
    /// the plaintext token to the HQ ingest endpoint; the cloud verifies by hashing and comparing. The
    /// plaintext is shown to the admin only once (at provisioning / rotation) and never stored.</summary>
    public string? SyncSecretHash { get; private set; }

    private Tenant() { } // EF

    public static Tenant Create(string slug, string displayName, DateTimeOffset now,
        Guid? tenantId = null, Guid? primaryStoreId = null)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Display name is required.", nameof(displayName));
        return new Tenant
        {
            Id = tenantId ?? Uuid7.NewGuid(),
            Slug = NormalizeSlug(slug),
            DisplayName = displayName.Trim(),
            PrimaryStoreId = primaryStoreId ?? Uuid7.NewGuid(),
            IsActive = true,
            CreatedAtUtc = now,
        };
    }

    public void Rename(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Display name is required.", nameof(displayName));
        DisplayName = displayName.Trim();
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;

    /// <summary>Set (or rotate) the store→cloud sync secret. Pass the SHA-256 hex of the new token.</summary>
    public void SetSyncSecretHash(string sha256Hex)
    {
        if (string.IsNullOrWhiteSpace(sha256Hex)) throw new ArgumentException("Sync secret hash is required.", nameof(sha256Hex));
        SyncSecretHash = sha256Hex.Trim().ToLowerInvariant();
    }

    /// <summary>Slugs reserved for the platform itself — never assignable to a tenant.</summary>
    public static readonly IReadOnlySet<string> ReservedSlugs = new HashSet<string>(StringComparer.Ordinal)
    {
        "www", "admin", "api", "app", "pos", "mail", "smtp", "ftp", "ns", "ns1", "ns2",
        "status", "help", "support", "billing", "static", "assets", "cdn", "hq", "console", "dashboard",
    };

    /// <summary>
    /// Canonical subdomain label: lowercase, DNS-safe (a–z, 0–9, single internal hyphens), 2–63 chars,
    /// not a reserved platform name. Throws on anything that can't be a valid subdomain.
    /// </summary>
    public static string NormalizeSlug(string? slug)
    {
        var raw = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (raw.Length is < 2 or > 63) throw new ArgumentException("Slug must be 2–63 characters.", nameof(slug));

        var sb = new StringBuilder(raw.Length);
        char prev = '\0';
        foreach (var c in raw)
        {
            var ok = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
            if (!ok) throw new ArgumentException($"Slug '{raw}' may contain only letters, digits and hyphens.", nameof(slug));
            if (c == '-' && (prev == '-' || prev == '\0'))
                throw new ArgumentException("Slug may not start with, end with, or repeat a hyphen.", nameof(slug));
            sb.Append(c);
            prev = c;
        }
        if (prev == '-') throw new ArgumentException("Slug may not start with, end with, or repeat a hyphen.", nameof(slug));

        var result = sb.ToString();
        if (ReservedSlugs.Contains(result)) throw new ArgumentException($"Slug '{result}' is reserved.", nameof(slug));
        return result;
    }
}
