using Microsoft.EntityFrameworkCore;
using Pos.Application.Inventory;
using Pos.Domain.Inventory;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class TransferRepository : ITransferRepository
{
    private readonly PosDbContext _db;
    public TransferRepository(PosDbContext db) => _db = db;

    public async Task AddAsync(StockTransfer transfer, CancellationToken ct = default) =>
        await _db.StockTransfers.AddAsync(transfer, ct);

    public Task<StockTransfer?> GetAsync(Guid tenantId, Guid storeId, Guid transferId, CancellationToken ct = default) =>
        _db.StockTransfers.AsSplitQuery()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.StoreId == storeId && t.Id == transferId, ct);

    public async Task<IReadOnlyList<StockTransfer>> ListRecentAsync(Guid tenantId, Guid storeId, int take, CancellationToken ct = default) =>
        await _db.StockTransfers.AsSplitQuery()
            .Where(t => t.TenantId == tenantId && t.StoreId == storeId)
            .OrderByDescending(t => t.DispatchedAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
}

internal sealed class IncomingTransferRepository : IIncomingTransferRepository
{
    private readonly PosDbContext _db;
    public IncomingTransferRepository(PosDbContext db) => _db = db;

    public Task<IncomingTransfer?> GetAsync(Guid tenantId, Guid storeId, Guid transferId, CancellationToken ct = default) =>
        _db.IncomingTransfers.AsSplitQuery()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.StoreId == storeId && t.Id == transferId, ct);

    public Task<bool> ExistsAsync(Guid tenantId, Guid storeId, Guid transferId, CancellationToken ct = default) =>
        _db.IncomingTransfers.AnyAsync(t => t.TenantId == tenantId && t.StoreId == storeId && t.Id == transferId, ct);

    public async Task<IReadOnlyList<IncomingTransfer>> ListPendingAsync(Guid tenantId, Guid storeId, CancellationToken ct = default) =>
        await _db.IncomingTransfers.AsSplitQuery()
            .Where(t => t.TenantId == tenantId && t.StoreId == storeId && t.Status == IncomingTransferStatus.Pending)
            .OrderBy(t => t.DispatchedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<IncomingTransfer>> ListRecentAsync(Guid tenantId, Guid storeId, int take, CancellationToken ct = default) =>
        await _db.IncomingTransfers.AsSplitQuery()
            .Where(t => t.TenantId == tenantId && t.StoreId == storeId)
            .OrderByDescending(t => t.DispatchedAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

    public async Task AddAsync(IncomingTransfer transfer, CancellationToken ct = default) =>
        await _db.IncomingTransfers.AddAsync(transfer, ct);
}
