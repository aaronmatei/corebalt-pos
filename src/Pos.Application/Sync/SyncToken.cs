using System.Security.Cryptography;

namespace Pos.Application.Sync;

/// <summary>
/// The store→cloud sync credential. A high-entropy opaque token: the cloud stores only its SHA-256 hash
/// (on the tenant registry row), the on-prem store keeps the plaintext and presents it on every push.
/// </summary>
public static class SyncToken
{
    /// <summary>Generate a fresh URL-safe token (256 bits of entropy).</summary>
    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return "hqs_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>SHA-256 (lowercase hex) of a token — what the cloud persists and compares against.</summary>
    public static string Hash(string token)
    {
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token ?? ""));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>Constant-time check of a presented token against a stored hash.</summary>
    public static bool Verify(string? presented, string? storedHash)
    {
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(storedHash)) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(Hash(presented));
        var b = System.Text.Encoding.UTF8.GetBytes(storedHash);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
