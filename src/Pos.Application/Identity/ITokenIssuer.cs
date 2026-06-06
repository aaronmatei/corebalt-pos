using Pos.Domain.Identity;

namespace Pos.Application.Identity;

/// <summary>Issues a signed JWT for a user (tenant/store/user/role/name/staff-code claims).</summary>
public interface ITokenIssuer
{
    AccessToken Issue(User user);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAtUtc);
