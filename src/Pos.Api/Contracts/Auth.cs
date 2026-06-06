using Pos.Domain.Identity;

namespace Pos.Api.Contracts;

public sealed record PinLoginRequest(string StaffCode, string Pin);
public sealed record LoginRequest(string Username, string Password);
public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, string TokenType = "Bearer");
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record CreateUserRequest(
    string Name, string Username, string StaffCode, UserRole Role, string? Pin, string? Password);

public sealed record UserResponse(Guid Id, string Name, string Username, string StaffCode, UserRole Role, bool IsActive);
