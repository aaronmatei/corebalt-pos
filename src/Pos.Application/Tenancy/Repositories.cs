using Pos.Domain.Tenancy;

namespace Pos.Application.Tenancy;

/// <summary>The HQ/cloud subdomain → tenant registry (Hq mode only). Looked up on every request, so
/// the impl should be cheap; the slug is the unique key.</summary>
public interface ITenantRepository
{
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Tenant tenant, CancellationToken ct = default);
}

public interface IMerchantProfileRepository
{
    Task<MerchantProfile?> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(MerchantProfile profile, CancellationToken ct = default);
}

public interface IMpesaSettingsRepository
{
    Task<MpesaSettings?> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(MpesaSettings settings, CancellationToken ct = default);
}

public interface IEtimsSettingsRepository
{
    Task<EtimsSettings?> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(EtimsSettings settings, CancellationToken ct = default);
}

public interface IEntitlementsRepository
{
    Task<Entitlements?> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(Entitlements entitlements, CancellationToken ct = default);
}

public interface IRegisterRepository
{
    Task<Register?> GetAsync(Guid tenantId, Guid storeId, Guid registerId, CancellationToken ct = default);

    /// <summary>Return the lane for this id, or register it on first use with the next sequential
    /// number/name ("Lane N"). Added to the unit of work; the caller's SaveChanges commits it.</summary>
    Task<Register> GetOrCreateAsync(Guid tenantId, Guid storeId, Guid registerId, CancellationToken ct = default);
}

public interface IOpsSettingsRepository
{
    Task<OpsSettings?> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(OpsSettings settings, CancellationToken ct = default);
}
