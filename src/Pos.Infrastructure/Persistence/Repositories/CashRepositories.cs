using Microsoft.EntityFrameworkCore;
using Pos.Application.Cash;
using Pos.Domain.Cash;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class RegisterSessionRepository : IRegisterSessionRepository
{
    private readonly PosDbContext _db;
    public RegisterSessionRepository(PosDbContext db) => _db = db;

    public Task<RegisterSession?> GetOpenAsync(Guid tenantId, Guid storeId, Guid registerId, CancellationToken ct = default) =>
        _db.RegisterSessions.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.StoreId == storeId
            && s.RegisterId == registerId && s.Status == SessionStatus.Open, ct);

    public Task<RegisterSession?> GetAsync(Guid tenantId, Guid storeId, Guid sessionId, CancellationToken ct = default) =>
        _db.RegisterSessions.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.StoreId == storeId && s.Id == sessionId, ct);

    public async Task AddAsync(RegisterSession session, CancellationToken ct = default) =>
        await _db.RegisterSessions.AddAsync(session, ct);

    public async Task<IReadOnlyList<RegisterSession>> ListAsync(Guid tenantId, Guid storeId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? registerId, CancellationToken ct = default) =>
        await _db.RegisterSessions
            .Where(s => s.TenantId == tenantId && s.StoreId == storeId
                && s.OpenedAtUtc >= fromUtc && s.OpenedAtUtc < toUtc
                && (registerId == null || s.RegisterId == registerId))
            .OrderByDescending(s => s.OpenedAtUtc)
            .ToListAsync(ct);
}

internal sealed class CashMovementRepository : ICashMovementRepository
{
    private readonly PosDbContext _db;
    public CashMovementRepository(PosDbContext db) => _db = db;

    public async Task AddAsync(CashMovement movement, CancellationToken ct = default) =>
        await _db.CashMovements.AddAsync(movement, ct);

    public async Task<IReadOnlyList<CashMovement>> ListBySessionAsync(Guid tenantId, Guid storeId, Guid sessionId, CancellationToken ct = default) =>
        await _db.CashMovements
            .Where(m => m.TenantId == tenantId && m.StoreId == storeId && m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(ct);
}
