using System.Security.Cryptography;
using System.Text;
using Pos.Application.Abstractions;

namespace Pos.Infrastructure.Security;

/// <summary>
/// AES-256-GCM encryption for per-tenant integration secrets at rest. The key is derived (SHA-256)
/// from a config value (the install's secret). Output is "g1:" + base64(nonce|tag|ciphertext); empty
/// input passes through, and Unprotect tolerates legacy plaintext (no "g1:" prefix).
/// </summary>
public sealed class AesSecretProtector : ISecretProtector
{
    private const string Prefix = "g1:";
    private const int NonceLen = 12;
    private const int TagLen = 16;
    private readonly byte[] _key;

    public AesSecretProtector(string key) => _key = SHA256.HashData(Encoding.UTF8.GetBytes(key ?? ""));

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[TagLen];
        using var gcm = new AesGcm(_key, TagLen);
        gcm.Encrypt(nonce, pt, ct, tag);

        var blob = new byte[NonceLen + TagLen + ct.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceLen);
        ct.CopyTo(blob, NonceLen + TagLen);
        return Prefix + Convert.ToBase64String(blob);
    }

    public string Unprotect(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext) || !ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
            return ciphertext; // empty or legacy plaintext
        var blob = Convert.FromBase64String(ciphertext[Prefix.Length..]);
        var nonce = blob.AsSpan(0, NonceLen);
        var tag = blob.AsSpan(NonceLen, TagLen);
        var ct = blob.AsSpan(NonceLen + TagLen);
        var pt = new byte[ct.Length];
        using var gcm = new AesGcm(_key, TagLen);
        gcm.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }
}
