using Pos.Application.Abstractions;
using Pos.Application.Sync;
using Pos.Domain.Tenancy;

namespace Pos.Application.Tenancy;

/// <summary>
/// Admin-provisions a NEW tenant on the HQ/cloud tier (the vendor-controlled onboarding path — there is
/// no self-serve /setup wizard in Hq mode). Creates the subdomain registry row, then reuses
/// <see cref="SetupService"/> to lay down the merchant profile, baseline (or licensed) entitlements and
/// the first manager — all in ONE transaction, scoped to the new tenant's generated ids.
/// </summary>
public sealed record ProvisionTenantRequest(
    string Slug, string DisplayName, string KraPin,
    bool VatRegistered, string? VatNumber,
    string? Phone, string? Email, string? Address, string? Currency,
    string? LicenseKey,
    string ManagerName, string ManagerUsername, string ManagerPassword);

/// <summary><see cref="SyncToken"/> is the store→cloud credential, returned in PLAINTEXT exactly once
/// here — the operator configures it on the on-prem store server; the cloud keeps only its hash.</summary>
public sealed record ProvisionedTenant(Guid TenantId, Guid StoreId, string Slug, string DisplayName, string SyncToken);

public sealed class TenantProvisioningService
{
    private readonly ITenantRepository _tenants;
    private readonly SetupService _setup;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public TenantProvisioningService(ITenantRepository tenants, SetupService setup, IClock clock, IUnitOfWork uow)
    {
        _tenants = tenants;
        _setup = setup;
        _clock = clock;
        _uow = uow;
    }

    /// <summary>Rotate a tenant's store→cloud sync token. The old token stops working immediately; the new
    /// plaintext is returned once (configure it on the store servers). Null if the slug is unknown.</summary>
    public async Task<string?> RotateSyncTokenAsync(string slug, CancellationToken ct = default)
    {
        var tenant = await _tenants.GetBySlugAsync(Tenant.NormalizeSlug(slug), ct);
        if (tenant is null) return null;
        var token = SyncToken.Generate();
        tenant.SetSyncSecretHash(SyncToken.Hash(token));
        await _uow.SaveChangesAsync(ct);
        return token;
    }

    public async Task<ProvisionedTenant> ProvisionAsync(ProvisionTenantRequest r, CancellationToken ct = default)
    {
        var slug = Tenant.NormalizeSlug(r.Slug); // throws ArgumentException on malformed/reserved
        if (string.IsNullOrWhiteSpace(r.DisplayName)) throw new ArgumentException("Display name is required.", nameof(r.DisplayName));
        if (string.IsNullOrWhiteSpace(r.KraPin)) throw new ArgumentException("KRA PIN is required.", nameof(r.KraPin));
        if (string.IsNullOrWhiteSpace(r.ManagerUsername) || string.IsNullOrWhiteSpace(r.ManagerPassword))
            throw new ArgumentException("Manager username and password are required.");
        if (await _tenants.SlugExistsAsync(slug, ct))
            throw new InvalidOperationException($"Subdomain '{slug}' is already taken.");

        var tenant = Tenant.Create(slug, r.DisplayName, _clock.UtcNow);
        var syncToken = SyncToken.Generate();
        tenant.SetSyncSecretHash(SyncToken.Hash(syncToken)); // store only the hash; return plaintext once
        await _tenants.AddAsync(tenant, ct); // tracked; committed by SetupService's single SaveChanges below

        var setup = new ProvisionRequest(
            LegalName: r.DisplayName, TradingName: r.DisplayName, KraPin: r.KraPin,
            VatRegistered: r.VatRegistered, VatNumber: r.VatNumber,
            Phone: r.Phone ?? "", Email: r.Email, Address: r.Address ?? "",
            Currency: string.IsNullOrWhiteSpace(r.Currency) ? "KES" : r.Currency!,
            BranchName: r.DisplayName, BranchCode: "HQ", BranchAddress: r.Address ?? "",
            ReceiptFooter: null, ShowPoweredBy: true,
            MpesaEnabled: false, MpesaShortCode: null, MpesaConsumerKey: null, MpesaConsumerSecret: null,
            MpesaPasskey: null, MpesaEnvironment: MpesaEnvironment.Sandbox,
            EtimsEnabled: false, EtimsMode: EtimsMode.Vscu, EtimsDeviceSerial: null, EtimsBranchId: null,
            EtimsCmcKey: null, EtimsBaseUrl: null,
            LicenseKey: r.LicenseKey,
            ManagerName: r.ManagerName, ManagerUsername: r.ManagerUsername, ManagerPassword: r.ManagerPassword);

        await _setup.ProvisionAsync(tenant.Id, tenant.PrimaryStoreId, setup, ct);

        return new ProvisionedTenant(tenant.Id, tenant.PrimaryStoreId, tenant.Slug, tenant.DisplayName, syncToken);
    }
}
