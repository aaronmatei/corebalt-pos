using Microsoft.EntityFrameworkCore;
using Pos.Application.Payments;
using Pos.Domain.Payments;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class MpesaPaymentRepository : IMpesaPaymentRepository
{
    private readonly PosDbContext _db;
    public MpesaPaymentRepository(PosDbContext db) => _db = db;

    public async Task AddAsync(MpesaPayment payment, CancellationToken ct = default) =>
        await _db.MpesaPayments.AddAsync(payment, ct);

    public Task<MpesaPayment?> FindBySaleIdAsync(Guid tenantId, Guid storeId, Guid saleId, CancellationToken ct = default) =>
        _db.MpesaPayments
            .Where(p => p.TenantId == tenantId && p.StoreId == storeId && p.SaleId == saleId)
            .OrderByDescending(p => p.InitiatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public Task<MpesaPayment?> FindByCheckoutRequestIdAsync(string checkoutRequestId, CancellationToken ct = default) =>
        _db.MpesaPayments.FirstOrDefaultAsync(p => p.CheckoutRequestId == checkoutRequestId, ct);
}
