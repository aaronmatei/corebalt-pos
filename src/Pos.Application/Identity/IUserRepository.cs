using Pos.Domain.Identity;

namespace Pos.Application.Identity;

public interface IUserRepository
{
    Task AddAsync(User user, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid tenantId, Guid storeId, Guid userId, CancellationToken ct = default);
    Task<User?> FindByUsernameAsync(Guid tenantId, string username, CancellationToken ct = default);
    Task<User?> FindByStaffCodeAsync(Guid tenantId, Guid storeId, string staffCode, CancellationToken ct = default);
    Task<bool> AnyManagerExistsAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);
    Task<bool> UsernameExistsAsync(Guid tenantId, string username, CancellationToken ct = default);
    Task<IReadOnlyList<User>> ListAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);

    /// <summary>Load a user WITH its enrolled fingerprints (for enrol/list/remove).</summary>
    Task<User?> GetByIdWithFingerprintsAsync(Guid tenantId, Guid storeId, Guid userId, CancellationToken ct = default);

    /// <summary>Active users that have at least one enrolled fingerprint, WITH their fingerprints loaded —
    /// the candidate set for a 1:N identify.</summary>
    Task<IReadOnlyList<User>> ListActiveWithFingerprintsAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);

    /// <summary>Persist a newly-enrolled fingerprint (explicit Added — it attaches to an EXISTING user).</summary>
    Task AddFingerprintAsync(FingerprintCredential credential, CancellationToken ct = default);

    /// <summary>Mark an enrolled fingerprint for deletion.</summary>
    void RemoveFingerprint(FingerprintCredential credential);
}
