using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Identity;

/// <summary>
/// A staff member who can log into the till / back office. Tenant+store scoped like every fact.
/// Holds only HASHES — never plaintext PIN/password. The PIN is a short numeric code for fast till
/// login; the password is for the back office. Hashing/verification live behind an Application port
/// (IPasswordHasher); the aggregate only stores and swaps the resulting hashes.
/// </summary>
public sealed class User : AggregateRoot, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    public string StaffCode { get; private set; } = string.Empty;
    public string? PinHash { get; private set; }
    public string? PasswordHash { get; private set; }
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public bool MustChangePassword { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // Enrolled fingerprints (OPTIONAL fast sign-in; PIN/password remain the fallback). Templates only,
    // encrypted at rest. Exposed read-only; mutated via Enroll/RemoveFingerprint so consent is enforced.
    private readonly List<FingerprintCredential> _fingerprints = new();
    public IReadOnlyCollection<FingerprintCredential> Fingerprints => _fingerprints.AsReadOnly();
    public bool HasFingerprints => _fingerprints.Count > 0;

    private User() { } // EF

    public static User Create(Guid tenantId, Guid storeId, string name, string username, string staffCode, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username is required.", nameof(username));
        if (string.IsNullOrWhiteSpace(staffCode)) throw new ArgumentException("Staff code is required.", nameof(staffCode));

        return new User
        {
            Id = Uuid7.NewGuid(),
            TenantId = tenantId,
            StoreId = storeId,
            Name = name.Trim(),
            Username = username.Trim().ToLowerInvariant(),
            StaffCode = staffCode.Trim(),
            Role = role,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Set the password hash. <paramref name="mustChange"/> forces a change on next login (seeding).</summary>
    public void SetPasswordHash(string passwordHash, bool mustChange = false)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        PasswordHash = passwordHash;
        MustChangePassword = mustChange;
    }

    public void SetPinHash(string pinHash)
    {
        if (string.IsNullOrWhiteSpace(pinHash)) throw new ArgumentException("PIN hash is required.", nameof(pinHash));
        PinHash = pinHash;
    }

    /// <summary>Replace the password (clears the must-change flag) — used by change-password on first login.</summary>
    public void ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash)) throw new ArgumentException("Password hash is required.", nameof(newPasswordHash));
        PasswordHash = newPasswordHash;
        MustChangePassword = false;
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;

    /// <summary>
    /// Enrol a fingerprint TEMPLATE (the reader already discarded the raw image). REFUSES without explicit
    /// consent — enrolment is a supervised, consented act. Records who enrolled it and when.
    /// </summary>
    public FingerprintCredential EnrollFingerprint(byte[] template, string? fingerLabel,
        Guid enrolledByUserId, bool consentGiven, DateTimeOffset now)
    {
        if (!consentGiven)
            throw new InvalidOperationException("Explicit consent is required to enrol a fingerprint.");
        var credential = FingerprintCredential.Create(Id, template, fingerLabel, enrolledByUserId, now);
        _fingerprints.Add(credential);
        return credential;
    }

    /// <summary>Remove an enrolled fingerprint (e.g. a cashier leaves, or re-enrols). Returns false if absent.</summary>
    public bool RemoveFingerprint(Guid fingerprintId)
    {
        var credential = _fingerprints.FirstOrDefault(f => f.Id == fingerprintId);
        if (credential is null) return false;
        _fingerprints.Remove(credential);
        return true;
    }
}
