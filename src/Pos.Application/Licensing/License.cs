using System.Text.Json;
using Pos.Domain.Tenancy;

namespace Pos.Application.Licensing;

/// <summary>
/// The entitlements a Corebalt-issued licence grants a specific tenant. Produced ONLY by verifying a
/// signed licence token — never assembled from client input — so a client can apply a key but cannot
/// edit edition/flags/limits to self-upgrade.
/// </summary>
public sealed record License(
    Guid TenantId,
    Edition Edition,
    Feature Features,
    int MaxTills,
    int MaxBranches,
    DateTimeOffset ValidUntil,
    DateTimeOffset IssuedAt);

/// <summary>
/// Wire format for a licence token: <c>base64url(payloadJson) "." base64url(signature)</c>. The payload
/// JSON is canonical (fixed field order) so the exact bytes that are signed are the bytes that are
/// verified. Shared by the verifier (app, public key) and the signer (vendor/tests, private key).
/// </summary>
public static class LicenseCodec
{
    private sealed record Dto(string t, int ed, int fl, int mt, int mb, long exp, long iat);

    public static byte[] PayloadBytes(License l) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dto(
            l.TenantId.ToString("N"), (int)l.Edition, (int)l.Features, l.MaxTills, l.MaxBranches,
            l.ValidUntil.ToUnixTimeSeconds(), l.IssuedAt.ToUnixTimeSeconds()));

    public static License Parse(byte[] payload)
    {
        var d = JsonSerializer.Deserialize<Dto>(payload) ?? throw new FormatException("Empty licence payload.");
        return new License(Guid.ParseExact(d.t, "N"), (Edition)d.ed, (Feature)d.fl, d.mt, d.mb,
            DateTimeOffset.FromUnixTimeSeconds(d.exp), DateTimeOffset.FromUnixTimeSeconds(d.iat));
    }

    public static string Encode(byte[] payload, byte[] signature) =>
        $"{B64Url(payload)}.{B64Url(signature)}";

    public static bool TrySplit(string token, out byte[] payload, out byte[] signature)
    {
        payload = []; signature = [];
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = token.Trim().Split('.');
        if (parts.Length != 2) return false;
        try { payload = FromB64Url(parts[0]); signature = FromB64Url(parts[1]); return true; }
        catch { return false; }
    }

    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return Convert.FromBase64String(s);
    }
}
