using Pos.Domain.Sales;

namespace Pos.Application.Sales;

public interface ICreditNoteRepository
{
    Task AddAsync(CreditNote creditNote, CancellationToken ct = default);
    Task<CreditNote?> GetAsync(Guid tenantId, Guid storeId, Guid creditNoteId, CancellationToken ct = default);

    /// <summary>Total quantity already returned per product across all credit notes for a sale (over-return guard).</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetReturnedQuantitiesAsync(Guid tenantId, Guid storeId, Guid originalSaleId, CancellationToken ct = default);
}
