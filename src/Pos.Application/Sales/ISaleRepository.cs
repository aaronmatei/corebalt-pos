using Pos.Domain.Sales;

namespace Pos.Application.Sales;

public interface ISaleRepository
{
    Task<Sale?> GetAsync(Guid tenantId, Guid storeId, Guid saleId, CancellationToken ct = default);
    Task AddAsync(Sale sale, CancellationToken ct = default);

    /// <summary>
    /// Sales in a given fiscal state across this store server's DB (NOT tenant-scoped — the eTIMS sync
    /// worker processes every sale on the box). Used to find Signed-but-not-Synced sales to transmit.
    /// </summary>
    Task<IReadOnlyList<Sale>> ListByFiscalStatusAsync(FiscalStatus status, int max, CancellationToken ct = default);
}
