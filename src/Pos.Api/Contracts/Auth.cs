using Pos.Domain.Identity;

namespace Pos.Api.Contracts;

public sealed record PinLoginRequest(string StaffCode, string Pin);
public sealed record LoginRequest(string Username, string Password);
public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, string TokenType = "Bearer");
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record CreateUserRequest(
    string Name, string Username, string StaffCode, UserRole Role, string? Pin, string? Password);

public sealed record UserResponse(Guid Id, string Name, string Username, string StaffCode, UserRole Role, bool IsActive);

// ── Fingerprint auth (optional; PIN stays the fallback). Templates/probes travel as base64. ──

/// <summary>A live probe captured at the till's reader (base64). The server identifies it locally.</summary>
public sealed record FingerprintLoginRequest(string Probe);

/// <summary>The same token PIN login issues, plus the resolved cashier label (the till doesn't decode the JWT).</summary>
public sealed record FingerprintLoginResponse(
    string AccessToken, DateTimeOffset ExpiresAtUtc, string StaffCode, string Name, string TokenType = "Bearer");

/// <summary>Manager enrols a cashier's fingerprint: the reader-captured template (base64) + explicit consent.</summary>
public sealed record EnrollFingerprintRequest(string Template, string? FingerLabel, bool Consent);

public sealed record FingerprintResponse(Guid Id, string? FingerLabel, DateTimeOffset EnrolledAtUtc, bool ConsentGiven);
