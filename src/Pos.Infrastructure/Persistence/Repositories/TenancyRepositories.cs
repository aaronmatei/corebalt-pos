using Microsoft.EntityFrameworkCore;
using Pos.Application.Tenancy;
using Pos.Domain.Tenancy;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class MerchantProfileRepository : IMerchantProfileRepository
{
    private readonly PosDbContext _db;
    public MerchantProfileRepository(PosDbContext db) => _db = db;

    public Task<MerchantProfile?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.MerchantProfiles.FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(MerchantProfile profile, CancellationToken ct = default) =>
        await _db.MerchantProfiles.AddAsync(profile, ct);
}

internal sealed class MpesaSettingsRepository : IMpesaSettingsRepository
{
    private readonly PosDbContext _db;
    public MpesaSettingsRepository(PosDbContext db) => _db = db;

    public Task<MpesaSettings?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.MpesaSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public async Task AddAsync(MpesaSettings settings, CancellationToken ct = default) =>
        await _db.MpesaSettings.AddAsync(settings, ct);
}

internal sealed class EtimsSettingsRepository : IEtimsSettingsRepository
{
    private readonly PosDbContext _db;
    public EtimsSettingsRepository(PosDbContext db) => _db = db;

    public Task<EtimsSettings?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.EtimsSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public async Task AddAsync(EtimsSettings settings, CancellationToken ct = default) =>
        await _db.EtimsSettings.AddAsync(settings, ct);
}

internal sealed class EntitlementsRepository : IEntitlementsRepository
{
    private readonly PosDbContext _db;
    public EntitlementsRepository(PosDbContext db) => _db = db;

    public Task<Entitlements?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.Entitlements.FirstOrDefaultAsync(e => e.TenantId == tenantId, ct);

    public async Task AddAsync(Entitlements entitlements, CancellationToken ct = default) =>
        await _db.Entitlements.AddAsync(entitlements, ct);
}
