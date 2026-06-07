using Microsoft.Extensions.Logging;
using Pos.Application.Notifications;

namespace Pos.Infrastructure.Notifications;

/// <summary>
/// STUB SMS channel — the seam + config fields, not a real sender. Modelled on an HTTP SMS gateway
/// (e.g. Africa's Talking). When configured it logs what it WOULD send; the real gateway client drops in
/// here later (reading per-client settings). Disabled by default, so the dispatcher skips it cleanly.
/// </summary>
internal sealed class SmsNotificationChannel : INotificationChannel
{
    private readonly SmsChannelOptions _options;
    private readonly ILogger<SmsNotificationChannel> _log;

    public SmsNotificationChannel(SmsChannelOptions options, ILogger<SmsNotificationChannel> log)
    {
        _options = options;
        _log = log;
    }

    public string Channel => "Sms";
    public bool IsEnabled => _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.ApiKey)
        && !string.IsNullOrWhiteSpace(_options.ToNumber);

    public Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        // TODO(channels): real gateway POST via _options once a client configures it.
        _log.LogInformation("notification.sms.stub to={To} title={Title}", _options.ToNumber, message.Title);
        return Task.CompletedTask;
    }
}
