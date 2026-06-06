using Pos.Application.Abstractions;
using Pos.Application.Licensing;
using Pos.Domain.Tenancy;

namespace Pos.Application.Tenancy;

/// <summary>Reads the current tenant's licence to gate optional modules/UI. Expiry is honoured.</summary>
public interface IEntitlements
{
    Task<bool> HasAsync(Feature feature, CancellationToken ct = default);
    Task<Entitlements?> CurrentAsync(CancellationToken ct = default);
}

/// <summary>
/// The runtime AUTHORITY for what a tenant may use is the signed licence key — re-verified on every
/// read — NOT the stored entitlement columns. So a client who edits the DB rows directly gains nothing:
/// without a valid Corebalt-signed key they fall back to the unlicensed baseline (no optional features).
/// </summary>
public sealed class EntitlementsService : IEntitlements
{
    private readonly ICurrentContext _ctx;
    private readonly IEntitlementsRepository _repo;
    private readonly ILicenseVerifier _verifier;
    private readonly IClock _clock;

    public EntitlementsService(ICurrentContext ctx, IEntitlementsRepository repo, ILicenseVerifier verifier, IClock clock)
    {
        _ctx = ctx;
        _repo = repo;
        _verifier = verifier;
        _clock = clock;
    }

    public async Task<Entitlements?> CurrentAsync(CancellationToken ct = default)
    {
        var row = await _repo.GetAsync(_ctx.TenantId, ct);
        if (row is null) return null;
        if (string.IsNullOrWhiteSpace(row.LicenseKey)) return Entitlements.Unlicensed(_ctx.TenantId);

        var result = _verifier.Verify(row.LicenseKey!, _ctx.TenantId, _clock.UtcNow);
        return result.Ok
            ? Entitlements.FromLicense(_ctx.TenantId, result.License!.Edition, result.License.Features,
                result.License.MaxTills, result.License.MaxBranches, row.LicenseKey!, result.License.ValidUntil)
            : Entitlements.Unlicensed(_ctx.TenantId); // tampered / expired / wrong tenant → deny
    }

    public async Task<bool> HasAsync(Feature feature, CancellationToken ct = default)
    {
        var ent = await CurrentAsync(ct);
        return ent is not null && ent.Has(feature, _clock.UtcNow);
    }
}
