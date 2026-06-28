using Microsoft.EntityFrameworkCore;
using Pos.Application.Printing;
using Pos.Application.Tenancy;
using Pos.Domain.Tenancy;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class TenantRepository : ITenantRepository
{
    private readonly PosDbContext _db;
    public TenantRepository(PosDbContext db) => _db = db;

    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);

    public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        _db.Tenants.AnyAsync(t => t.Slug == slug, ct);

    public async Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken ct = default) =>
        await _db.Tenants.OrderBy(t => t.Slug).ToListAsync(ct);

    public async Task AddAsync(Tenant tenant, CancellationToken ct = default) =>
        await _db.Tenants.AddAsync(tenant, ct);
}

internal sealed class OpsSettingsRepository : IOpsSettingsRepository
{
    private readonly PosDbContext _db;
    public OpsSettingsRepository(PosDbContext db) => _db = db;

    public Task<OpsSettings?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.OpsSettings.FirstOrDefaultAsync(o => o.TenantId == tenantId, ct);

    public async Task AddAsync(OpsSettings settings, CancellationToken ct = default) =>
        await _db.OpsSettings.AddAsync(settings, ct);
}

internal sealed class RegisterRepository : IRegisterRepository
{
    private readonly PosDbContext _db;
    public RegisterRepository(PosDbContext db) => _db = db;

    public Task<Register?> GetAsync(Guid tenantId, Guid storeId, Guid registerId, CancellationToken ct = default) =>
        _db.Registers.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.StoreId == storeId && r.Id == registerId, ct);

    public async Task<Register> GetOrCreateAsync(Guid tenantId, Guid storeId, Guid registerId, CancellationToken ct = default)
    {
        var existing = await GetAsync(tenantId, storeId, registerId, ct);
        if (existing is not null) return existing;

        var n = await _db.Registers.CountAsync(r => r.TenantId == tenantId && r.StoreId == storeId, ct) + 1;
        var register = Register.Create(tenantId, storeId, registerId, n.ToString(), $"Lane {n}");
        await _db.Registers.AddAsync(register, ct);
        return register;
    }
}

internal sealed class PrinterProfileRepository : IPrinterProfileRepository
{
    private readonly PosDbContext _db;
    public PrinterProfileRepository(PosDbContext db) => _db = db;

    public Task<PrinterProfile?> GetByRegisterAsync(Guid tenantId, Guid registerId, CancellationToken ct = default) =>
        _db.PrinterProfiles.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.RegisterId == registerId, ct);

    public async Task AddAsync(PrinterProfile profile, CancellationToken ct = default) =>
        await _db.PrinterProfiles.AddAsync(profile, ct);
}

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
