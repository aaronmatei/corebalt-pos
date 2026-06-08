using Microsoft.EntityFrameworkCore;
using Pos.Application.Customers;
using Pos.Domain.Customers;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class CustomerRepository : ICustomerRepository
{
    private readonly PosDbContext _db;
    public CustomerRepository(PosDbContext db) => _db = db;

    public Task<Customer?> GetAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
        _db.Customers.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == customerId, ct);

    public Task<Customer?> GetByPhoneAsync(Guid tenantId, string normalizedPhone, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(normalizedPhone)
            ? Task.FromResult<Customer?>(null)
            : _db.Customers.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Phone == normalizedPhone, ct);

    public async Task<IReadOnlyList<Customer>> SearchAsync(Guid tenantId, string? query, bool includeInactive = false, int max = 50, CancellationToken ct = default)
    {
        var q = _db.Customers.Where(c => c.TenantId == tenantId && (includeInactive || c.IsActive));
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(c => EF.Functions.ILike(c.Name, $"%{term}%") || (c.Phone != null && c.Phone.Contains(term)));
        }
        return await q.OrderBy(c => c.Name).Take(Math.Clamp(max, 1, 200)).ToListAsync(ct);
    }

    public Task<bool> PhoneExistsAsync(Guid tenantId, string normalizedPhone, Guid? excludingCustomerId = null, CancellationToken ct = default) =>
        _db.Customers.AnyAsync(c => c.TenantId == tenantId && c.Phone == normalizedPhone
            && (excludingCustomerId == null || c.Id != excludingCustomerId), ct);

    public async Task AddAsync(Customer customer, CancellationToken ct = default) =>
        await _db.Customers.AddAsync(customer, ct);
}
