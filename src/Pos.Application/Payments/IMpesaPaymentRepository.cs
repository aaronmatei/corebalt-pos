using Pos.Domain.Payments;

namespace Pos.Application.Payments;

public interface IMpesaPaymentRepository
{
    Task AddAsync(MpesaPayment payment, CancellationToken ct = default);

    /// <summary>The most recent M-Pesa attempt for a sale (a sale may be retried after a failure).</summary>
    Task<MpesaPayment?> FindBySaleIdAsync(Guid tenantId, Guid storeId, Guid saleId, CancellationToken ct = default);

    /// <summary>
    /// Lookup by Daraja's globally-unique CheckoutRequestID — the callback's only correlation key,
    /// so this is intentionally NOT tenant/store scoped (the record carries its own scope).
    /// </summary>
    Task<MpesaPayment?> FindByCheckoutRequestIdAsync(string checkoutRequestId, CancellationToken ct = default);
}
