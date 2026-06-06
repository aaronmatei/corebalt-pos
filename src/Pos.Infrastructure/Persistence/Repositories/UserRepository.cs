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
}
