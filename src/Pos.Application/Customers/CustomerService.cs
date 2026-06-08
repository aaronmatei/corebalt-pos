using Pos.Application.Abstractions;
using Pos.Domain.Customers;

namespace Pos.Application.Customers;

/// <summary>
/// Customer / loyalty-member use cases, shared by the JSON API and the Blazor back-office. Phone is the
/// natural key for the till's fast lookup, so a duplicate normalized phone throws (→ 409). Not-found
/// returns null so the caller picks 404 / an inline message.
/// </summary>
public sealed class CustomerService
{
    private readonly ICurrentContext _ctx;
    private readonly ICustomerRepository _customers;
    private readonly IUnitOfWork _uow;

    public CustomerService(ICurrentContext ctx, ICustomerRepository customers, IUnitOfWork uow)
    {
        _ctx = ctx;
        _customers = customers;
        _uow = uow;
    }

    public Task<Customer?> GetAsync(Guid id, CancellationToken ct = default) =>
        _customers.GetAsync(_ctx.TenantId, id, ct);

    public Task<IReadOnlyList<Customer>> SearchAsync(string? query, bool includeInactive = false, int max = 50, CancellationToken ct = default) =>
        _customers.SearchAsync(_ctx.TenantId, query, includeInactive, max, ct);

    /// <summary>Resolve a customer by phone — the till's attach-at-checkout path.</summary>
    public Task<Customer?> FindByPhoneAsync(string phone, CancellationToken ct = default) =>
        _customers.GetByPhoneAsync(_ctx.TenantId, KenyanIdValidator.NormalizePhone(phone ?? ""), ct);

    public async Task<Customer> CreateAsync(string name, string? phone, string? email, string? kraPin, string? nationalId, CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(phone) ? null : KenyanIdValidator.NormalizePhone(phone);
        if (normalized is not null && await _customers.PhoneExistsAsync(_ctx.TenantId, normalized, ct: ct))
            throw new InvalidOperationException($"A customer with phone {normalized} already exists.");

        var customer = Customer.Create(_ctx.TenantId, name, phone, email, kraPin, nationalId);
        await _customers.AddAsync(customer, ct);
        await _uow.SaveChangesAsync(ct);
        return customer;
    }

    public async Task<Customer?> UpdateAsync(Guid id, string name, string? phone, string? email,
        string? kraPin, string? nationalId, bool isActive, CancellationToken ct = default)
    {
        var customer = await _customers.GetAsync(_ctx.TenantId, id, ct);
        if (customer is null) return null;

        var normalized = string.IsNullOrWhiteSpace(phone) ? null : KenyanIdValidator.NormalizePhone(phone);
        if (normalized is not null && await _customers.PhoneExistsAsync(_ctx.TenantId, normalized, excludingCustomerId: id, ct: ct))
            throw new InvalidOperationException($"A customer with phone {normalized} already exists.");

        customer.UpdateContact(name, phone, email, kraPin, nationalId);
        if (isActive) customer.Reactivate(); else customer.Deactivate();
        await _uow.SaveChangesAsync(ct);
        return customer;
    }

    /// <summary>Manager correction of a loyalty balance (positive or negative).</summary>
    public async Task<Customer?> AdjustPointsAsync(Guid id, int delta, CancellationToken ct = default)
    {
        var customer = await _customers.GetAsync(_ctx.TenantId, id, ct);
        if (customer is null) return null;
        customer.AdjustPoints(delta);
        await _uow.SaveChangesAsync(ct);
        return customer;
    }
}
