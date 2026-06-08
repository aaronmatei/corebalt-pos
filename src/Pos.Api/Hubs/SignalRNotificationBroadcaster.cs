using Microsoft.AspNetCore.SignalR;
using Pos.Application.Notifications;

namespace Pos.Api.Hubs;

/// <summary>
/// Pushes notifications to the (tenant, store) group over <see cref="NotificationHub"/>. Best-effort: a
/// transport hiccup is swallowed so it never breaks the notification feed write that triggered it.
/// </summary>
internal sealed class SignalRNotificationBroadcaster : INotificationBroadcaster
{
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<SignalRNotificationBroadcaster> _log;

    public SignalRNotificationBroadcaster(IHubContext<NotificationHub> hub, ILogger<SignalRNotificationBroadcaster> log)
    {
        _hub = hub;
        _log = log;
    }

    public async Task NotifyAsync(Guid tenantId, Guid storeId, RealtimeNotification message, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients.Group(NotificationHub.GroupName(tenantId, storeId))
                .SendAsync("notification", message, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Real-time notification push failed (feed write already committed).");
        }
    }
}
