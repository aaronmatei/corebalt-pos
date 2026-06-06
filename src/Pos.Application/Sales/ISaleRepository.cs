using Pos.Domain.Sales;

namespace Pos.Application.Sales;

public interface ISaleRepository
{
    Task<Sale?> GetAsync(Guid tenantId, Guid storeId, Guid saleId, CancellationToken ct = default);
    Task AddAsync(Sale sale, CancellationToken ct = default);
}
