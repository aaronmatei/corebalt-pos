using System.Security.Cryptography;

namespace Pos.Application.Licensing;

/// <summary>
/// Signs a <see cref="License"/> into a token with Corebalt's PRIVATE key. This is the VENDOR side —
/// used by the licence-generation tool and the tests. The product never calls this (it has no private
/// key); it only <see cref="LicenseVerifier">verifies</see>. Kept here so signer and verifier share one
/// canonical payload format.
/// </summary>
public static class LicenseSigner
{
    public static string Sign(License license, string privateKeyPkcs8Base64)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyPkcs8Base64), out _);
        var payload = LicenseCodec.PayloadBytes(license);
        var signature = ecdsa.SignData(payload, HashAlgorithmName.SHA256);
        return LicenseCodec.Encode(payload, signature);
    }
}
