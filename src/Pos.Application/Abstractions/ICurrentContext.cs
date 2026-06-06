namespace Pos.Application.Abstractions;

/// <summary>
/// The authenticated caller's tenant/store/user. Sourced from the request pipeline
/// (step 3 API). Handlers read it instead of trusting client-supplied scope ids —
/// that's how the tenant-scoping and store-authoritative invariants survive a hostile
/// or buggy client.
/// </summary>
public interface ICurrentContext
{
    Guid TenantId { get; }
    Guid StoreId { get; }
    Guid UserId { get; }
}
