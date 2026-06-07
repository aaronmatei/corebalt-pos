using Pos.Application.Identity;

namespace Pos.Infrastructure.Identity;

/// <summary>
/// Dev/test stand-in for a real reader SDK — exercises the whole seam WITHOUT hardware. "Extraction" is a
/// pass-through (the captured bytes ARE the template) and "identify" is an exact byte-match against the
/// candidates. The dev capture convention is base64(UTF8("STUB:" + staffCode)) at the edges (till + the
/// enrolment page), so enrol + sign-in round-trip deterministically. The real SDK (DigitalPersona, ZKTeco,
/// SecuGen, Futronic, …) replaces this with real template extraction + fuzzy minutiae matching; nothing
/// above this interface changes.
/// </summary>
internal sealed class StubFingerprintAuthenticator : IFingerprintAuthenticator
{
    private readonly FingerprintOptions _options;
    public StubFingerprintAuthenticator(FingerprintOptions options) => _options = options;

    public bool IsEnabled => _options.Enabled;
    public string Provider => "stub";

    public byte[] ExtractTemplate(byte[] capturedSample)
    {
        if (capturedSample is null || capturedSample.Length == 0)
            throw new ArgumentException("Empty fingerprint capture.", nameof(capturedSample));
        return capturedSample; // no feature extraction in the stub — the capture is treated as the template
    }

    public Guid? Identify(byte[] probeSample, IReadOnlyList<FingerprintCandidate> candidates)
    {
        if (probeSample is null || probeSample.Length == 0) return null;
        foreach (var candidate in candidates)
            if (candidate.Template.AsSpan().SequenceEqual(probeSample))
                return candidate.UserId;
        return null;
    }
}
