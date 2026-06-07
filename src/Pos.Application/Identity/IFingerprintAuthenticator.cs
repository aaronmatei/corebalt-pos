namespace Pos.Application.Identity;

/// <summary>An enrolled template to match a live probe against (one per enrolled finger).</summary>
public sealed record FingerprintCandidate(Guid UserId, byte[] Template);

/// <summary>
/// The fingerprint-reader SDK seam. A specific reader (DigitalPersona/HID U.are.U, ZKTeco, SecuGen,
/// Futronic — common in Kenya) plugs in behind this; a stub backs dev/test. Two responsibilities, both
/// LOCAL/on-prem — biometrics never go to the cloud:
/// <list type="bullet">
/// <item>extract a storable TEMPLATE from a reader capture (the raw image is discarded at the reader);</item>
/// <item>identify a live probe against the enrolled candidates (1:N match) and return the matched user.</item>
/// </list>
/// Deliberately NOT Windows Hello — that authenticates the Windows account, not the many app cashiers
/// sharing one till. Capture happens at the device with the reader (the till for sign-in, the enrolment
/// station for enrolment); matching happens here on the store server against the encrypted templates.
/// </summary>
public interface IFingerprintAuthenticator
{
    /// <summary>True when fingerprint auth is wired on this install (a reader/SDK is configured). When
    /// false, sign-in/enrolment are unavailable and PIN remains the only path.</summary>
    bool IsEnabled { get; }

    /// <summary>Identify the SDK in logs / diagnostics (e.g. "stub", "digitalpersona").</summary>
    string Provider { get; }

    /// <summary>Validate + normalise a reader-captured sample into the template bytes to persist.
    /// Throws <see cref="ArgumentException"/> if the capture is unusable.</summary>
    byte[] ExtractTemplate(byte[] capturedSample);

    /// <summary>Local 1:N match of a probe against the enrolled candidates. Returns the matched UserId,
    /// or null when there is no confident match.</summary>
    Guid? Identify(byte[] probeSample, IReadOnlyList<FingerprintCandidate> candidates);
}
