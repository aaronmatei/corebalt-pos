namespace Pos.Application.Fiscalization;

public enum EtimsMode { Vscu, Oscu }

/// <summary>
/// eTIMS configuration (config section "Etims"). Plain POCO — the API composition root populates it.
/// DeviceSerial/BranchId/CmcKey/BaseUrl are blank placeholders for real onboarding; the seller PIN
/// comes from the Store config, not here. With <see cref="Enabled"/> + no real credentials, the host
/// wires the fake provider (training-mode fiscalization).
/// </summary>
public sealed class EtimsOptions
{
    public bool Enabled { get; set; }
    public EtimsMode Mode { get; set; } = EtimsMode.Vscu;
    public string DeviceSerial { get; set; } = "";
    public string BranchId { get; set; } = "";
    public string CmcKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";

    // Sync-worker tuning (the seam for the real KRA batch upload).
    public int SyncIntervalSeconds { get; set; } = 30;
    public int SyncMaxAttempts { get; set; } = 5;

    /// <summary>True once real onboarding credentials are present — switch from fake to the real provider.</summary>
    public bool HasRealCredentials =>
        !string.IsNullOrWhiteSpace(CmcKey) && !string.IsNullOrWhiteSpace(DeviceSerial);
}
