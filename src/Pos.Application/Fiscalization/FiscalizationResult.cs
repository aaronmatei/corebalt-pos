namespace Pos.Application.Fiscalization;

/// <summary>Outcome of a sign or sync call. CUIN/Signature/QrData are minted by the CU/eTIMS.</summary>
public sealed record FiscalizationResult(
    bool Success,
    string? Cuin,
    string? Signature,
    string? QrData,
    DateTimeOffset? SignedAtUtc,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static FiscalizationResult Ok(string cuin, string signature, string qrData, DateTimeOffset signedAtUtc) =>
        new(true, cuin, signature, qrData, signedAtUtc);

    public static FiscalizationResult Fail(string code, string message) =>
        new(false, null, null, null, null, code, message);
}
