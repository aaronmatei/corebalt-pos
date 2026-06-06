using System.Security.Cryptography;

namespace Pos.Application.Licensing;

public sealed record LicenseVerification(bool Ok, License? License, string? Error)
{
    public static LicenseVerification Valid(License l) => new(true, l, null);
    public static LicenseVerification Invalid(string error) => new(false, null, error);
}

/// <summary>
/// Verifies a Corebalt-signed licence token with the embedded PUBLIC key (ECDSA P-256 / SHA-256). The
/// private key never ships in the product — only Corebalt can mint a key, so a tampered or self-made
/// token fails the signature check and an expired one is rejected.
/// </summary>
public interface ILicenseVerifier
{
    LicenseVerification Verify(string token, Guid expectedTenant, DateTimeOffset now);
}

public sealed class LicenseVerifier : ILicenseVerifier
{
    // Corebalt's licence-signing PUBLIC key (SubjectPublicKeyInfo, base64). The matching private key is
    // held only by the vendor (the licence-gen tool / tests) — it is intentionally NOT in this codebase.
    private const string PublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEqWEBKjW8t50S7RzyuOCJLcJXeRJGFU+WU03mEDV3nU4hdl/7sHoZYc/LpiroVK8P7bVccLo2Ky3NKpjpQz9rBg==";

    public LicenseVerification Verify(string token, Guid expectedTenant, DateTimeOffset now)
    {
        if (!LicenseCodec.TrySplit(token, out var payload, out var signature))
            return LicenseVerification.Invalid("Malformed licence key.");

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyBase64), out _);
        if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256))
            return LicenseVerification.Invalid("Licence signature is invalid (tampered or not issued by Corebalt).");

        License license;
        try { license = LicenseCodec.Parse(payload); }
        catch { return LicenseVerification.Invalid("Unreadable licence payload."); }

        if (license.TenantId != expectedTenant)
            return LicenseVerification.Invalid("Licence was issued for a different tenant.");
        if (now > license.ValidUntil)
            return LicenseVerification.Invalid($"Licence expired on {license.ValidUntil:yyyy-MM-dd}.");

        return LicenseVerification.Valid(license);
    }
}
