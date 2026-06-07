using Microsoft.EntityFrameworkCore;
using Pos.Application.Sales;
using Pos.Domain.Sales;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class CreditNoteRepository : ICreditNoteRepository
{
    private readonly PosDbContext _db;
    public CreditNoteRepository(PosDbContext db) => _db = db;

    public async Task AddAsync(CreditNote creditNote, CancellationToken ct = default) =>
        await _db.CreditNotes.AddAsync(creditNote, ct);

    public Task<CreditNote?> GetAsync(Guid tenantId, Guid storeId, Guid creditNoteId, CancellationToken ct = default) =>
        _db.CreditNotes.FirstOrDefaultAsync(
            c => c.TenantId == tenantId && c.StoreId == storeId && c.Id == creditNoteId, ct);

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetReturnedQuantitiesAsync(
        Guid tenantId, Guid storeId, Guid originalSaleId, CancellationToken ct = default)
    {
        // Owned line collections load with the aggregate; aggregate the prior returns per product.
        var notes = await _db.CreditNotes
            .Where(c => c.TenantId == tenantId && c.StoreId == storeId && c.OriginalSaleId == originalSaleId)
            .ToListAsync(ct);

        return notes
            .SelectMany(n => n.Lines)
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
    }

    public async Task<IReadOnlyList<CreditNote>> ListBySessionAsync(Guid tenantId, Guid storeId, Guid sessionId, CancellationToken ct = default) =>
        await _db.CreditNotes
            .Where(c => c.TenantId == tenantId && c.StoreId == storeId && c.RegisterSessionId == sessionId)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CreditNote>> ListBetweenAsync(Guid tenantId, Guid storeId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default) =>
        await _db.CreditNotes
            .Where(c => c.TenantId == tenantId && c.StoreId == storeId
                && c.CreatedAtUtc >= fromUtc && c.CreatedAtUtc < toUtc)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync(ct);
}
