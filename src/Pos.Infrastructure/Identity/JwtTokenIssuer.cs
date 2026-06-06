using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Pos.Application.Abstractions;
using Pos.Application.Identity;
using Pos.Domain.Identity;

namespace Pos.Infrastructure.Identity;

/// <summary>Issues HS256 JWTs carrying tenant/store/user/role/name/staff-code (+ must-change-password).</summary>
public sealed class JwtTokenIssuer : ITokenIssuer
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;

    public JwtTokenIssuer(JwtOptions options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    public AccessToken Issue(User user)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.LifetimeMinutes);
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)), SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(PosClaims.TenantId, user.TenantId.ToString()),
            new(PosClaims.StoreId, user.StoreId.ToString()),
            new(PosClaims.Name, user.Name),
            new(PosClaims.StaffCode, user.StaffCode),
            new(PosClaims.Role, user.Role.ToString()),
            new(PosClaims.MustChangePassword, user.MustChangePassword ? "true" : "false"),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
