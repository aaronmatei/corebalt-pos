namespace Pos.Infrastructure.Identity;

/// <summary>
/// Host config for fingerprint auth (section "Fingerprint"). It's an OPTIONAL method — PIN is always the
/// fallback — but the dev stub defaults ON so the seam works out of the box. A real install sets
/// Provider to the chosen reader SDK once hardware is selected.
/// </summary>
public sealed class FingerprintOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Which SDK backs the authenticator. "stub" (dev) today; e.g. "digitalpersona" later.</summary>
    public string Provider { get; set; } = "stub";
}
