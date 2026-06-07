using Microsoft.Extensions.Logging;
using Pos.Application.Notifications;

namespace Pos.Infrastructure.Notifications;

/// <summary>
/// STUB Email channel — the seam + config fields, not a real sender. When SMTP is configured
/// (<see cref="EmailChannelOptions.Enabled"/>) it logs what it WOULD send; the real SMTP client drops in
/// here later (reading per-client settings). Disabled by default, so the dispatcher skips it cleanly.
/// </summary>
internal sealed class EmailNotificationChannel : INotificationChannel
{
    private readonly EmailChannelOptions _options;
    private readonly ILogger<EmailNotificationChannel> _log;

    public EmailNotificationChannel(EmailChannelOptions options, ILogger<EmailNotificationChannel> log)
    {
        _options = options;
        _log = log;
    }

    public string Channel => "Email";
    public bool IsEnabled => _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.SmtpHost)
        && !string.IsNullOrWhiteSpace(_options.ToAddress);

    public Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        // TODO(channels): real SMTP send via _options once a client configures it.
        _log.LogInformation("notification.email.stub to={To} title={Title} body={Body}",
            _options.ToAddress, message.Title, message.Body);
        return Task.CompletedTask;
    }
}
