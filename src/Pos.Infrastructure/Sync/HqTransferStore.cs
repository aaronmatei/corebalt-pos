using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Sync;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Sync;

internal sealed class HqTransferStore : IHqTransferStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PosDbContext _db;
    private readonly IClock _clock;

    public HqTransferStore(PosDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TransferSnapshot>> IncomingAsync(Guid tenantId, Guid toStoreId, CancellationToken ct = default)
    {
        var rows = await _db.HqTransfers
            .Where(t => t.TenantId == tenantId && t.ToStoreId == toStoreId && !t.IsReceived)
            .OrderBy(t => t.DispatchedAtUtc)
            .ToListAsync(ct);

        return rows.Select(t => new TransferSnapshot(
            t.Id, t.TenantId, t.FromStoreId, t.ToStoreId, t.ToStoreName, t.DispatchedByName, t.DispatchedAtUtc, t.Note,
            JsonSerializer.Deserialize<List<TransferLineSnapshot>>(t.LinesJson, Json) ?? [])).ToList();
    }

    public async Task<bool> MarkReceivedAsync(Guid tenantId, Guid toStoreId, Guid transferId, CancellationToken ct = default)
    {
        var transfer = await _db.HqTransfers
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == transferId && t.ToStoreId == toStoreId, ct);
        if (transfer is null) return false;
        transfer.MarkReceived(_clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<BranchDto>> BranchesAsync(Guid tenantId, Guid exceptStoreId, CancellationToken ct = default) =>
        await _db.HqBranches
            .Where(x => x.TenantId == tenantId && x.StoreId != exceptStoreId)
            .OrderBy(x => x.Name)
            .Select(x => new BranchDto(x.StoreId, x.Name))
            .ToListAsync(ct);
}
