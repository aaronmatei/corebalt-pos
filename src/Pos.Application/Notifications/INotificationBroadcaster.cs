namespace Pos.Application.Notifications;

/// <summary>A real-time notification pushed to connected back-office clients (e.g. via SignalR).</summary>
public sealed record RealtimeNotification(string Type, string Title, string Body, int UnreadCount);

/// <summary>
/// Pushes a just-created notification to live clients so the back-office badge/feed updates without a
/// poll. Abstracted so the Application/Infrastructure layers don't depend on SignalR: the API binds the
/// real (hub-backed) implementation; a no-op default keeps console/test hosts working. Implementations
/// MUST be best-effort — a transport failure must never break notification persistence.
/// </summary>
public interface INotificationBroadcaster
{
    Task NotifyAsync(Guid tenantId, Guid storeId, RealtimeNotification message, CancellationToken ct = default);
}
