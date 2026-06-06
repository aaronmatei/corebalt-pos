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
}
