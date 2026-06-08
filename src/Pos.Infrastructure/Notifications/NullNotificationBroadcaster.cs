using Pos.Application.Notifications;

namespace Pos.Infrastructure.Notifications;

/// <summary>
/// Default no-op broadcaster: notifications still persist to the feed, there's just no live push. The API
/// host replaces this with the SignalR-backed implementation; console/test hosts keep this one.
/// </summary>
internal sealed class NullNotificationBroadcaster : INotificationBroadcaster
{
    public Task NotifyAsync(Guid tenantId, Guid storeId, RealtimeNotification message, CancellationToken ct = default) =>
        Task.CompletedTask;
}
