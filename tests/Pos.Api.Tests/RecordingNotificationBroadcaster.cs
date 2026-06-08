using System.Collections.Concurrent;
using Pos.Application.Notifications;

namespace Pos.Api.Tests;

/// <summary>
/// Test double for <see cref="INotificationBroadcaster"/>: records every real-time push so a test can
/// assert the in-app channel fired one (the actual SignalR delivery is framework behaviour). Registered as
/// a singleton in the shared fixture; tests run sequentially in the collection, so clear it before use.
/// </summary>
public sealed class RecordingNotificationBroadcaster : INotificationBroadcaster
{
    public sealed record Captured(Guid TenantId, Guid StoreId, RealtimeNotification Message);

    private readonly ConcurrentQueue<Captured> _pushes = new();
    public IReadOnlyList<Captured> Pushes => _pushes.ToArray();

    public void Clear() => _pushes.Clear();

    public Task NotifyAsync(Guid tenantId, Guid storeId, RealtimeNotification message, CancellationToken ct = default)
    {
        _pushes.Enqueue(new Captured(tenantId, storeId, message));
        return Task.CompletedTask;
    }
}
