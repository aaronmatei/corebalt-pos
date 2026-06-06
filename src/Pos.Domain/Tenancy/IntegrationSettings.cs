using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Tenancy;

public enum MpesaEnvironment { Sandbox = 0, Production = 1 }
public enum EtimsMode { Vscu = 0, Oscu = 1 }

/// <summary>
/// Per-tenant M-Pesa (Daraja) credentials. Secrets (Passkey, ConsumerSecret) are encrypted at rest by
/// an EF value converter; the domain only ever sees plaintext. One row per tenant.
/// </summary>
public sealed class MpesaSettings : Entity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public bool Enabled { get; private set; }
    public string ShortCode { get; private set; } = string.Empty;
    public string ConsumerKey { get; private set; } = string.Empty;
    public string ConsumerSecret { get; private set; } = string.Empty; // encrypted at rest
    public string Passkey { get; private set; } = string.Empty;        // encrypted at rest
    public MpesaEnvironment Environment { get; private set; }

    private MpesaSettings() { } // EF

    public static MpesaSettings Create(Guid tenantId) =>
        new() { Id = Uuid7.NewGuid(), TenantId = tenantId, Environment = MpesaEnvironment.Sandbox };

    public void Configure(bool enabled, string shortCode, string consumerKey, string consumerSecret, string passkey, MpesaEnvironment environment)
    {
        Enabled = enabled;
        ShortCode = (shortCode ?? "").Trim();
        ConsumerKey = (consumerKey ?? "").Trim();
        ConsumerSecret = (consumerSecret ?? "").Trim();
        Passkey = (passkey ?? "").Trim();
        Environment = environment;
    }

    public bool IsConfigured =>
        Enabled && ShortCode.Length > 0 && ConsumerKey.Length > 0 && ConsumerSecret.Length > 0 && Passkey.Length > 0;
}

/// <summary>Per-tenant KRA eTIMS device/branch credentials. CmcKey is encrypted at rest.</summary>
public sealed class EtimsSettings : Entity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public bool Enabled { get; private set; }
    public EtimsMode Mode { get; private set; }
    public string DeviceSerial { get; private set; } = string.Empty;
    public string BranchId { get; private set; } = string.Empty;
    public string CmcKey { get; private set; } = string.Empty;        // encrypted at rest
    public string BaseUrl { get; private set; } = string.Empty;

    private EtimsSettings() { } // EF

    public static EtimsSettings Create(Guid tenantId) =>
        new() { Id = Uuid7.NewGuid(), TenantId = tenantId, Mode = EtimsMode.Vscu };

    public void Configure(bool enabled, EtimsMode mode, string deviceSerial, string branchId, string cmcKey, string baseUrl)
    {
        Enabled = enabled;
        Mode = mode;
        DeviceSerial = (deviceSerial ?? "").Trim();
        BranchId = (branchId ?? "").Trim();
        CmcKey = (cmcKey ?? "").Trim();
        BaseUrl = (baseUrl ?? "").Trim();
    }

    /// <summary>True once real onboarding credentials exist — switch from the fake/training provider to real.</summary>
    public bool HasRealCredentials => CmcKey.Length > 0 && DeviceSerial.Length > 0;
}
