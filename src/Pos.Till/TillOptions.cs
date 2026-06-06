namespace Pos.Till;

/// <summary>
/// Till configuration, bound from appsettings.json (section "Till") or POS_TILL_* env vars.
/// The identity GUIDs default to the dev tenant/store/user the persistence demo seeds, so a
/// freshly-launched till talks to the same store scope as `dotnet run --project samples/Pos.Persistence.Demo`.
/// RegisterId identifies this lane; it travels in the checkout body, not a header.
/// </summary>
public sealed class TillOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5080";
    public Guid TenantId { get; set; } = Guid.Parse("019e9a8a-0000-7000-8000-000000000001");
    public Guid StoreId { get; set; } = Guid.Parse("019e9a8a-0000-7000-8000-000000000002");
    public Guid UserId { get; set; } = Guid.Parse("019e9a8a-0000-7000-8000-000000000003");
    public Guid RegisterId { get; set; } = Guid.Parse("019e9a8a-0000-7000-8000-0000000000a1");
    public string Currency { get; set; } = "KES";
}
