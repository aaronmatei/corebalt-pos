namespace Pos.Infrastructure.Notifications;

/// <summary>
/// Config seam for the Email channel (stub today). The real sender reads SMTP settings — for a per-client
/// install these would move to per-tenant DB settings (like M-Pesa/eTIMS), editable in back-office; for
/// now they're install-level config placeholders so the wiring + fields exist. <see cref="Enabled"/> stays
/// false until a client actually configures SMTP, so the dispatcher skips this channel.
/// </summary>
public sealed class EmailChannelOptions
{
    public bool Enabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    /// <summary>Where low-stock alerts are emailed (e.g. the store manager / buyer).</summary>
    public string? ToAddress { get; set; }
}

/// <summary>
/// Config seam for the SMS channel (stub today). Modelled on an HTTP SMS gateway such as Africa's Talking
/// (username + API key + sender id). Same per-tenant upgrade path as <see cref="EmailChannelOptions"/>.
/// </summary>
public sealed class SmsChannelOptions
{
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? Username { get; set; }
    public string? ApiKey { get; set; }
    public string? SenderId { get; set; }
    /// <summary>Destination MSISDN for low-stock alerts.</summary>
    public string? ToNumber { get; set; }
}
