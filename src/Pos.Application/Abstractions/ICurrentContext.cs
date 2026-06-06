using Pos.Domain.Identity;

namespace Pos.Application.Abstractions;

/// <summary>
/// The authenticated caller — sourced from the JWT (claims) in production, or a dev-header bypass for
/// tests/local. Handlers read it instead of trusting client-supplied scope ids; that's how the
/// tenant-scoping / store-authoritative invariants survive a hostile or buggy client. Now also carries
/// the staff identity (role/name/staff code) used for authorization and the receipt's cashier line.
/// </summary>
public interface ICurrentContext
{
    Guid TenantId { get; }
    Guid StoreId { get; }
    Guid UserId { get; }
    UserRole Role { get; }
    string UserName { get; }
    string StaffCode { get; }
}
