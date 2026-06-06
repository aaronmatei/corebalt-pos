using Pos.Application.Abstractions;
using Pos.Domain.Identity;

namespace Pos.Application.Identity;

/// <summary>
/// Login + user/password lifecycle for the store-server's tenant/store. Login returns null on ANY
/// failure (unknown staff/user, inactive, no credential set, or mismatch) — the endpoint maps that to
/// a single 401 so we never leak which part was wrong.
/// </summary>
public sealed class AuthService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenIssuer _tokens;
    private readonly StoreServerOptions _server;
    private readonly IUnitOfWork _uow;

    public AuthService(IUserRepository users, IPasswordHasher hasher, ITokenIssuer tokens,
        StoreServerOptions server, IUnitOfWork uow)
    {
        _users = users;
        _hasher = hasher;
        _tokens = tokens;
        _server = server;
        _uow = uow;
    }

    public async Task<AccessToken?> PinLoginAsync(string staffCode, string pin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(staffCode) || string.IsNullOrWhiteSpace(pin)) return null;
        var user = await _users.FindByStaffCodeAsync(_server.TenantId, _server.StoreId, staffCode.Trim(), ct);
        if (user is null || !user.IsActive || user.PinHash is null) return null;
        return _hasher.Verify(user.PinHash, pin) ? _tokens.Issue(user) : null;
    }

    public async Task<AccessToken?> PasswordLoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;
        var user = await _users.FindByUsernameAsync(_server.TenantId, username.Trim().ToLowerInvariant(), ct);
        if (user is null || !user.IsActive || user.PasswordHash is null) return null;
        return _hasher.Verify(user.PasswordHash, password) ? _tokens.Issue(user) : null;
    }

    public async Task<User> CreateUserAsync(string name, string username, string staffCode, UserRole role,
        string? pin, string? password, CancellationToken ct = default)
    {
        username = (username ?? "").Trim().ToLowerInvariant();
        staffCode = (staffCode ?? "").Trim();
        if (await _users.UsernameExistsAsync(_server.TenantId, username, ct))
            throw new InvalidOperationException($"Username '{username}' is already taken.");
        if (await _users.FindByStaffCodeAsync(_server.TenantId, _server.StoreId, staffCode, ct) is not null)
            throw new InvalidOperationException($"Staff code '{staffCode}' is already taken.");

        var user = User.Create(_server.TenantId, _server.StoreId, name, username, staffCode, role);
        if (!string.IsNullOrWhiteSpace(pin)) user.SetPinHash(_hasher.Hash(pin));
        if (!string.IsNullOrWhiteSpace(password)) user.SetPasswordHash(_hasher.Hash(password));
        await _users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>Returns false when the current password doesn't match; throws on a too-weak new password.</summary>
    public async Task<bool> ChangePasswordAsync(Guid tenantId, Guid storeId, Guid userId,
        string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(tenantId, storeId, userId, ct);
        if (user is null || user.PasswordHash is null) return false;
        if (!_hasher.Verify(user.PasswordHash, currentPassword)) return false;
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            throw new ArgumentException("New password must be at least 6 characters.");
        user.ChangePassword(_hasher.Hash(newPassword));
        await _uow.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Seed the first Manager (config username + default password, must-change) if none exists.</summary>
    public async Task EnsureBootstrapManagerAsync(string username, string password, CancellationToken ct = default)
    {
        if (await _users.AnyManagerExistsAsync(_server.TenantId, _server.StoreId, ct)) return;
        var manager = User.Create(_server.TenantId, _server.StoreId, "Store Manager", username, "0000", UserRole.Manager);
        manager.SetPasswordHash(_hasher.Hash(password), mustChange: true);
        await _users.AddAsync(manager, ct);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>DEV ONLY: seed a demo cashier with a PIN so the till's PIN login is usable out of the box.</summary>
    public async Task EnsureDevCashierAsync(string name, string staffCode, string pin, CancellationToken ct = default)
    {
        if (await _users.FindByStaffCodeAsync(_server.TenantId, _server.StoreId, staffCode, ct) is not null) return;
        var username = $"cashier-{staffCode.ToLowerInvariant()}";
        if (await _users.UsernameExistsAsync(_server.TenantId, username, ct)) return;
        var cashier = User.Create(_server.TenantId, _server.StoreId, name, username, staffCode, UserRole.Cashier);
        cashier.SetPinHash(_hasher.Hash(pin));
        await _users.AddAsync(cashier, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
