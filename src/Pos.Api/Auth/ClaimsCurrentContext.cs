using System.Security.Claims;
using Pos.Application.Abstractions;
using Pos.Application.Identity;
using Pos.Domain.Identity;

namespace Pos.Api.Auth;

/// <summary>
/// ICurrentContext sourced from the authenticated principal's claims (the JWT, or the dev-header
/// bypass which synthesizes the same claims). Read only on authorized routes, so the claims are
/// guaranteed present by the time a handler touches it.
/// </summary>
public sealed class ClaimsCurrentContext : ICurrentContext
{
    private readonly IHttpContextAccessor _accessor;
    public ClaimsCurrentContext(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal User => _accessor.HttpContext?.User
        ?? throw new InvalidOperationException("ClaimsCurrentContext requires an active HTTP request.");

    public Guid TenantId => RequireGuid(PosClaims.TenantId);
    public Guid StoreId => RequireGuid(PosClaims.StoreId);
    public Guid UserId => RequireGuid(PosClaims.UserId);
    public UserRole Role => Enum.TryParse<UserRole>(User.FindFirstValue(PosClaims.Role), out var r) ? r : UserRole.Cashier;
    public string UserName => User.FindFirstValue(PosClaims.Name) ?? "";
    public string StaffCode => User.FindFirstValue(PosClaims.StaffCode) ?? "";

    private Guid RequireGuid(string claim) =>
        Guid.TryParse(User.FindFirstValue(claim), out var g) && g != Guid.Empty
            ? g
            : throw new InvalidOperationException($"Authenticated principal is missing the '{claim}' claim.");
}
