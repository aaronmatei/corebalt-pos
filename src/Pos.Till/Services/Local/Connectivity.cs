namespace Pos.Till.Services.Local;

/// <summary>
/// Tracks whether the store server is currently reachable. It holds no transport of its own — the
/// view-model nudges it from the outcome of real API calls (a transport failure flips it offline; any
/// success flips it back) and the background drain loop probes <c>/healthz</c> to notice recovery while
/// idle. <see cref="Changed"/> fires only on an actual transition so the UI updates without churn.
/// Starts optimistic (online); the first catalogue load corrects it.
/// </summary>
public sealed class Connectivity
{
    private readonly object _lock = new();
    private bool _isOnline = true;

    public bool IsOnline { get { lock (_lock) return _isOnline; } }

    /// <summary>Fires with the new state only when online/offline actually changes.</summary>
    public event Action<bool>? Changed;

    public void Report(bool online)
    {
        bool changed;
        lock (_lock)
        {
            changed = _isOnline != online;
            _isOnline = online;
        }
        if (changed) Changed?.Invoke(online);
    }
}
