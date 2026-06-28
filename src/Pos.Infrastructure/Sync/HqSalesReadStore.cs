using Microsoft.EntityFrameworkCore;
using Pos.Application.Sync;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Sync;

internal sealed class HqSalesReadStore : IHqSalesReadStore, IHqSessionsReadStore, IHqCreditNotesReadStore, IHqStockReadStore, IHqSyncStatusReadStore
{
    private readonly PosDbContext _db;
    public HqSalesReadStore(PosDbContext db) => _db = db;

    public async Task<HqSalesView> RecentAsync(Guid tenantId, int take, CancellationToken ct = default)
    {
        var q = _db.HqSales.Where(s => s.TenantId == tenantId);
        var count = await q.CountAsync(ct);
        var total = count == 0 ? 0m : await q.SumAsync(s => s.GrandTotal, ct);
        var storeCount = await q.Select(s => s.StoreId).Distinct().CountAsync(ct);
        var rows = await q
            .OrderByDescending(s => s.CompletedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(s => new HqSaleRow(s.Id, s.StoreId, s.ReceiptNumber, s.RegisterName, s.CashierName,
                s.GrandTotal, s.TotalVat, s.LineCount, s.Currency, s.CompletedAtUtc, s.SyncedAtUtc))
            .ToListAsync(ct);
        return new HqSalesView(count, total, storeCount, rows);
    }

    async Task<IReadOnlyList<HqSessionRow>> IHqSessionsReadStore.RecentAsync(Guid tenantId, int take, CancellationToken ct) =>
        await _db.HqSessions.Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.ClosedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(s => new HqSessionRow(s.Id, s.StoreId, s.RegisterId, s.RegisterLabel,
                s.OpenedByName, s.OpenedAtUtc, s.OpeningFloat, s.ClosedByName, s.ClosedAtUtc,
                s.CountedCash, s.ExpectedCash, s.Variance, s.VarianceAcknowledged, s.Currency))
            .ToListAsync(ct);

    async Task<IReadOnlyList<HqCreditNoteRow>> IHqCreditNotesReadStore.RecentAsync(Guid tenantId, int take, CancellationToken ct) =>
        await _db.HqCreditNotes.Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(c => new HqCreditNoteRow(c.Id, c.StoreId, c.ReturnNumber, c.OriginalReceiptNumber, c.Reason,
                c.IsVoid, c.AuthorizedByName, c.RefundMethod, c.RefundStatus, c.GrandTotal, c.Currency, c.LineCount, c.CreatedAtUtc))
            .ToListAsync(ct);

    async Task<IReadOnlyList<HqStockRow>> IHqStockReadStore.AllAsync(Guid tenantId, int take, CancellationToken ct) =>
        await _db.HqStockOnHand.Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.Name)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(s => new HqStockRow(s.StoreId, s.ProductId, s.Sku, s.Name, s.UnitOfMeasure, s.OnHand, s.LastMovementAtUtc))
            .ToListAsync(ct);

    async Task<HqSyncStatus> IHqSyncStatusReadStore.GetAsync(Guid tenantId, CancellationToken ct)
    {
        var stores = (await _db.SyncInbox.Where(e => e.TenantId == tenantId)
            .GroupBy(e => e.StoreId)
            .Select(g => new HqStoreSyncRow(
                g.Key, g.Count(), g.Max(e => e.ReceivedAtUtc), g.Max(e => e.OccurredAtUtc)))
            .ToListAsync(ct))
            .OrderByDescending(r => r.LastReceivedAtUtc)
            .ToList();

        return new HqSyncStatus(
            stores.Sum(s => s.Changes),
            stores.Count == 0 ? null : stores.Max(s => s.LastReceivedAtUtc),
            stores);
    }
}
