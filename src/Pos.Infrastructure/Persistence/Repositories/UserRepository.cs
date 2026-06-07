using Microsoft.EntityFrameworkCore;
using Pos.Application.Identity;
using Pos.Domain.Identity;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository : IUserRepository
{
    private readonly PosDbContext _db;
    public UserRepository(PosDbContext db) => _db = db;

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _db.Users.AddAsync(user, ct);

    public Task<User?> GetByIdAsync(Guid tenantId, Guid storeId, Guid userId, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.StoreId == storeId && u.Id == userId, ct);

    public Task<User?> FindByUsernameAsync(Guid tenantId, string username, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Username == username, ct);

    public Task<User?> FindByStaffCodeAsync(Guid tenantId, Guid storeId, string staffCode, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.StoreId == storeId && u.StaffCode == staffCode, ct);

    public Task<bool> AnyManagerExistsAsync(Guid tenantId, Guid storeId, CancellationToken ct = default) =>
        _db.Users.AnyAsync(u => u.TenantId == tenantId && u.StoreId == storeId && u.Role == UserRole.Manager, ct);

    public Task<bool> UsernameExistsAsync(Guid tenantId, string username, CancellationToken ct = default) =>
        _db.Users.AnyAsync(u => u.TenantId == tenantId && u.Username == username, ct);

    public async Task<IReadOnlyList<User>> ListAsync(Guid tenantId, Guid storeId, CancellationToken ct = default) =>
        await _db.Users
            .Where(u => u.TenantId == tenantId && u.StoreId == storeId)
            .OrderBy(u => u.Name)
            .ToListAsync(ct);

    public Task<User?> GetByIdWithFingerprintsAsync(Guid tenantId, Guid storeId, Guid userId, CancellationToken ct = default) =>
        _db.Users
            .Include(u => u.Fingerprints)
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.StoreId == storeId && u.Id == userId, ct);

    public async Task<IReadOnlyList<User>> ListActiveWithFingerprintsAsync(Guid tenantId, Guid storeId, CancellationToken ct = default) =>
        await _db.Users
            .Include(u => u.Fingerprints)
            .Where(u => u.TenantId == tenantId && u.StoreId == storeId && u.IsActive && u.Fingerprints.Any())
            .ToListAsync(ct);

    public async Task AddFingerprintAsync(FingerprintCredential credential, CancellationToken ct = default) =>
        await _db.FingerprintCredentials.AddAsync(credential, ct);

    public void RemoveFingerprint(FingerprintCredential credential) =>
        _db.FingerprintCredentials.Remove(credential);
}
