using Pos.Domain.Customers;

namespace Pos.Application.Customers;

public interface ICustomerRepository
{
    Task<Customer?> GetAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    /// <summary>Find by exact normalized phone (the till's fast attach-at-checkout path).</summary>
    Task<Customer?> GetByPhoneAsync(Guid tenantId, string normalizedPhone, CancellationToken ct = default);

    /// <summary>Search by name or phone substring (active only unless includeInactive). Capped by `max`.</summary>
    Task<IReadOnlyList<Customer>> SearchAsync(Guid tenantId, string? query, bool includeInactive = false, int max = 50, CancellationToken ct = default);

    Task<bool> PhoneExistsAsync(Guid tenantId, string normalizedPhone, Guid? excludingCustomerId = null, CancellationToken ct = default);

    Task AddAsync(Customer customer, CancellationToken ct = default);
}
