using Pos.Domain.Tenancy;

namespace Pos.Application.Tenancy;

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
