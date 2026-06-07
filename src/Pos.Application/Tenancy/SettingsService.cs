using Pos.Application.Abstractions;
using Pos.Application.Licensing;
using Pos.Domain.Tenancy;

namespace Pos.Application.Tenancy;

/// <summary>
/// Back-office (Manager) edits to a tenant's own integration settings, plus APPLYING a Corebalt licence
/// key. The client edits their M-Pesa/eTIMS creds freely; entitlements only ever change by applying a
/// signed key (verified here) — there is no path to set edition/flags/limits directly.
/// </summary>
public sealed class SettingsService
{
    private readonly IMpesaSettingsRepository _mpesa;
    private readonly IEtimsSettingsRepository _etims;
    private readonly IEntitlementsRepository _entitlements;
    private readonly IOpsSettingsRepository _opsSettings;
    private readonly ILicenseVerifier _licenses;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public SettingsService(IMpesaSettingsRepository mpesa, IEtimsSettingsRepository etims,
        IEntitlementsRepository entitlements, IOpsSettingsRepository opsSettings, ILicenseVerifier licenses,
        IClock clock, IUnitOfWork uow)
    {
        _mpesa = mpesa;
        _etims = etims;
        _entitlements = entitlements;
        _opsSettings = opsSettings;
        _licenses = licenses;
        _clock = clock;
        _uow = uow;
    }

    /// <summary>Set the off-machine backup copy location (external drive / network share).</summary>
    public async Task UpdateSecondBackupLocationAsync(Guid tenantId, string? path, CancellationToken ct = default)
    {
        var ops = await _opsSettings.GetAsync(tenantId, ct);
        if (ops is null) { ops = OpsSettings.Create(tenantId); await _opsSettings.AddAsync(ops, ct); }
        ops.SetSecondBackupLocation(path);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task UpdateMpesaAsync(Guid tenantId, bool enabled, string shortCode, string consumerKey,
        string consumerSecret, string passkey, MpesaEnvironment environment, CancellationToken ct = default)
    {
        var settings = await _mpesa.GetAsync(tenantId, ct);
        if (settings is null) { settings = MpesaSettings.Create(tenantId); await _mpesa.AddAsync(settings, ct); }
        // Blank secret fields keep the stored value (so the Manager needn't re-type secrets to toggle a flag).
        var cs = string.IsNullOrEmpty(consumerSecret) ? settings.ConsumerSecret : consumerSecret;
        var pk = string.IsNullOrEmpty(passkey) ? settings.Passkey : passkey;
        settings.Configure(enabled, shortCode, consumerKey, cs, pk, environment);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task UpdateEtimsAsync(Guid tenantId, bool enabled, EtimsMode mode, string deviceSerial,
        string branchId, string cmcKey, string baseUrl, CancellationToken ct = default)
    {
        var settings = await _etims.GetAsync(tenantId, ct);
        if (settings is null) { settings = EtimsSettings.Create(tenantId); await _etims.AddAsync(settings, ct); }
        var cmc = string.IsNullOrEmpty(cmcKey) ? settings.CmcKey : cmcKey;
        settings.Configure(enabled, mode, deviceSerial, branchId, cmc, baseUrl);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Verify and apply a Corebalt licence key. On success the tenant's entitlements are
    /// replaced from the signed payload; on failure nothing changes and the reason is returned.</summary>
    public async Task<LicenseVerification> ApplyLicenseAsync(Guid tenantId, string licenseKey, CancellationToken ct = default)
    {
        var result = _licenses.Verify(licenseKey, tenantId, _clock.UtcNow);
        if (!result.Ok) return result;

        var l = result.License!;
        var ent = await _entitlements.GetAsync(tenantId, ct);
        if (ent is null)
        {
            ent = Entitlements.FromLicense(tenantId, l.Edition, l.Features, l.MaxTills, l.MaxBranches, licenseKey.Trim(), l.ValidUntil);
            await _entitlements.AddAsync(ent, ct);
        }
        else
        {
            ent.ApplyLicense(l.Edition, l.Features, l.MaxTills, l.MaxBranches, licenseKey.Trim(), l.ValidUntil);
        }
        await _uow.SaveChangesAsync(ct);
        return result;
    }
}
