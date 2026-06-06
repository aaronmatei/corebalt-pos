namespace Pos.Application.Abstractions;

/// <summary>
/// Per-(tenant, store) monotonic receipt sequence, owned by the store server — NOT a global sequence
/// (that would break store-authoritative ownership and offline operation). The implementation
/// increments atomically and MUST join the caller's transaction so the number commits with the sale.
/// </summary>
public interface IReceiptNumberSequence
{
    Task<long> NextAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);
}
