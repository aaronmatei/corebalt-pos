using Pos.Domain.Sales;

namespace Pos.Application.Sales;

public interface ICreditNoteRepository
{
    Task AddAsync(CreditNote creditNote, CancellationToken ct = default);
    Task<CreditNote?> GetAsync(Guid tenantId, Guid storeId, Guid creditNoteId, CancellationToken ct = default);

    /// <summary>Total quantity already returned per product across all credit notes for a sale (over-return guard).</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetReturnedQuantitiesAsync(Guid tenantId, Guid storeId, Guid originalSaleId, CancellationToken ct = default);

    /// <summary>Credit notes belonging to one register session (cash-up / X-Z projection).</summary>
    Task<IReadOnlyList<CreditNote>> ListBySessionAsync(Guid tenantId, Guid storeId, Guid sessionId, CancellationToken ct = default);

    /// <summary>Credit notes issued within a UTC window (the store/day sales summary).</summary>
    Task<IReadOnlyList<CreditNote>> ListBetweenAsync(Guid tenantId, Guid storeId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}
