using System.Text;

namespace Pos.Till.Api;

/// <summary>
/// The till-side reader seam: captures a live fingerprint PROBE locally (the reader is attached to the
/// lane). The probe bytes are POSTed to the server, which does the 1:N match — the raw image never leaves
/// the reader. The real reader SDK implements this; <see cref="StubFingerprintScanner"/> backs dev/demo.
/// </summary>
public interface IFingerprintScanner
{
    /// <summary>Is a reader present on this till?</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Capture a probe template. <paramref name="devHint"/> (the staff code typed on the login screen)
    /// seeds the DEV stub so it matches what the back-office enrolled; a real reader ignores it and reads
    /// the finger. Returns null if nothing was captured.
    /// </summary>
    Task<byte[]?> CaptureAsync(string devHint, CancellationToken ct = default);
}

/// <summary>
/// Hardware-free stub: synthesises the same deterministic template the back-office enrols in dev —
/// base64(UTF8("STUB:" + staffCode)) — so "Scan fingerprint" round-trips end-to-end without a reader.
/// Requires the staff-code box to be filled (the dev stand-in for "which finger"); a real reader won't.
/// </summary>
public sealed class StubFingerprintScanner : IFingerprintScanner
{
    public bool IsAvailable => true;

    public Task<byte[]?> CaptureAsync(string devHint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(devHint)) return Task.FromResult<byte[]?>(null);
        return Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("STUB:" + devHint.Trim()));
    }
}
