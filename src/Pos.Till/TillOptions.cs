namespace Pos.Till;

/// <summary>
/// Till configuration, bound from appsettings.json (section "Till") or POS_TILL_* env vars.
/// Identity is NOT configured here any more — the cashier signs in with StaffCode + PIN and the
/// resulting JWT carries tenant/store/user. RegisterId identifies this physical lane; it travels in
/// the checkout body, not a header.
/// </summary>
public sealed class TillOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5080";
    public Guid RegisterId { get; set; } = Guid.Parse("019e9a8a-0000-7000-8000-0000000000a1");
    public string Currency { get; set; } = "KES";
}
