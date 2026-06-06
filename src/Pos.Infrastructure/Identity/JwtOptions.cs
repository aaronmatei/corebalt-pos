namespace Pos.Infrastructure.Identity;

/// <summary>
/// Local store-server JWT signing config (symmetric HMAC — works on the LAN with no internet). The
/// Key is a secret from config/user-secrets/env; Key must be ≥ 32 chars (256-bit) for HS256.
/// </summary>
public sealed class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "corebalt-pos";
    public string Audience { get; set; } = "corebalt-pos";
    public int LifetimeMinutes { get; set; } = 12 * 60; // one shift
}
