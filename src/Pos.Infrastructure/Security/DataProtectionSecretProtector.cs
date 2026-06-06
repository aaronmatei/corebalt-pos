using Microsoft.AspNetCore.DataProtection;
using Pos.Application.Abstractions;

namespace Pos.Infrastructure.Security;

/// <summary>
/// Encrypts per-tenant integration secrets at rest with ASP.NET Core Data Protection (the install-level
/// key ring on disk) — store ciphertext, never plaintext. Output is "dp:" + protected payload, so
/// <see cref="Unprotect"/> can tell ciphertext from any legacy plaintext and degrade gracefully.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Prefix = "dp:";
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("Pos.Tenancy.IntegrationSecrets.v1");

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        return Prefix + _protector.Protect(plaintext);
    }

    public string Unprotect(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext) || !ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
            return ciphertext; // empty or legacy plaintext
        try { return _protector.Unprotect(ciphertext[Prefix.Length..]); }
        catch { return ciphertext; } // unreadable (e.g. key rotated away) — don't crash reads
    }
}
