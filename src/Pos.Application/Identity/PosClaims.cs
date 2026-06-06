namespace Pos.Application.Identity;

/// <summary>JWT claim names carried in every token (and synthesized by the dev-header bypass).</summary>
public static class PosClaims
{
    public const string UserId = "sub";   // JwtRegisteredClaimNames.Sub
    public const string TenantId = "tid";
    public const string StoreId = "sid";
    public const string Name = "name";
    public const string StaffCode = "scd";
    public const string Role = "role";
    public const string MustChangePassword = "mcp";
}
