using Pos.Application.Abstractions;
using Pos.Application.Identity;
using Pos.Domain.Identity;
using Pos.Domain.Tenancy;

namespace Pos.Application.Tenancy;

/// <summary>
/// Everything the first-run wizard provisions for a fresh install: the merchant profile + first branch,
/// per-tenant M-Pesa + eTIMS settings, entitlements, and the FIRST manager (replaces hunting for seeded
/// credentials). One atomic save; marks setup complete. Tenant/store are passed explicitly (the wizard
/// runs anonymously against the install's configured StoreServer scope).
/// </summary>
public sealed record ProvisionRequest(
    string LegalName, string? TradingName, string KraPin, bool VatRegistered, string? VatNumber,
    string Phone, string? Email, string Address, string Currency,
    string BranchName, string BranchCode, string BranchAddress,
    string? ReceiptFooter, bool ShowPoweredBy,
    bool MpesaEnabled, string? MpesaShortCode, string? MpesaConsumerKey, string? MpesaConsumerSecret, string? MpesaPasskey, MpesaEnvironment MpesaEnvironment,
    bool EtimsEnabled, EtimsMode EtimsMode, string? EtimsDeviceSerial, string? EtimsBranchId, string? EtimsCmcKey, string? EtimsBaseUrl,
    Edition Edition, Feature Features, int MaxTills, int MaxBranches, string? LicenseKey, DateTimeOffset? ValidUntil,
    string ManagerName, string ManagerUsername, string ManagerPassword);

public sealed class SetupService
{
    private readonly IMerchantProfileRepository _merchants;
    private readonly IMpesaSettingsRepository _mpesa;
    private readonly IEtimsSettingsRepository _etims;
    private readonly IEntitlementsRepository _entitlements;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWork _uow;

    public SetupService(IMerchantProfileRepository merchants, IMpesaSettingsRepository mpesa,
        IEtimsSettingsRepository etims, IEntitlementsRepository entitlements, IUserRepository users,
        IPasswordHasher hasher, IUnitOfWork uow)
    {
        _merchants = merchants;
        _mpesa = mpesa;
        _etims = etims;
        _entitlements = entitlements;
        _users = users;
        _hasher = hasher;
        _uow = uow;
    }

    public async Task<bool> IsCompleteAsync(Guid tenantId, CancellationToken ct = default) =>
        (await _merchants.GetAsync(tenantId, ct))?.SetupComplete == true;

    public async Task ProvisionAsync(Guid tenantId, Guid storeId, ProvisionRequest r, CancellationToken ct = default)
    {
        if (await IsCompleteAsync(tenantId, ct))
            throw new InvalidOperationException("This install has already been set up.");

        var profile = MerchantProfile.Create(tenantId, r.LegalName, r.TradingName, r.KraPin, r.VatRegistered,
            r.VatNumber, r.Phone, r.Email, r.Address, r.Currency);
        profile.AddBranch(storeId, r.BranchName, r.BranchCode, r.BranchAddress); // first branch trades under the StoreId
        profile.SetBranding(logoUrl: null, r.ReceiptFooter, r.ShowPoweredBy);
        profile.MarkSetupComplete();
        await _merchants.AddAsync(profile, ct);

        var mpesa = MpesaSettings.Create(tenantId);
        mpesa.Configure(r.MpesaEnabled, r.MpesaShortCode ?? "", r.MpesaConsumerKey ?? "", r.MpesaConsumerSecret ?? "",
            r.MpesaPasskey ?? "", r.MpesaEnvironment);
        await _mpesa.AddAsync(mpesa, ct);

        var etims = EtimsSettings.Create(tenantId);
        etims.Configure(r.EtimsEnabled, r.EtimsMode, r.EtimsDeviceSerial ?? "", r.EtimsBranchId ?? "", r.EtimsCmcKey ?? "", r.EtimsBaseUrl ?? "");
        await _etims.AddAsync(etims, ct);

        var entitlements = Entitlements.Create(tenantId, r.Edition, r.Features, r.MaxTills, r.MaxBranches, r.LicenseKey, r.ValidUntil);
        await _entitlements.AddAsync(entitlements, ct);

        if (!string.IsNullOrWhiteSpace(r.ManagerUsername) && !string.IsNullOrWhiteSpace(r.ManagerPassword)
            && !await _users.UsernameExistsAsync(tenantId, r.ManagerUsername.Trim().ToLowerInvariant(), ct))
        {
            var manager = User.Create(tenantId, storeId, string.IsNullOrWhiteSpace(r.ManagerName) ? "Manager" : r.ManagerName,
                r.ManagerUsername, "0001", UserRole.Manager);
            manager.SetPasswordHash(_hasher.Hash(r.ManagerPassword));
            await _users.AddAsync(manager, ct);
        }

        await _uow.SaveChangesAsync(ct); // single transaction
    }
}
