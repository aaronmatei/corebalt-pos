namespace Pos.Application.Notifications;

/// <summary>
/// Turns pending low-stock outbox events into notifications via the registered channels. Implemented in
/// Infrastructure (it reads the outbox); driven by a background worker in the host and directly by tests
/// (mirrors the eTIMS sync seam). Idempotent — re-running won't duplicate a feed item.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>Dispatch one batch of pending events; returns how many were newly dispatched.</summary>
    Task<int> RunOnceAsync(CancellationToken ct = default);
}
