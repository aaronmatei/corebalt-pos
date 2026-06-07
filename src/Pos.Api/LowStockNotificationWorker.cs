using Pos.Application.Notifications;

namespace Pos.Api;

/// <summary>
/// Drains low-stock outbox events into notifications on a short interval. Thin loop over
/// <see cref="INotificationDispatcher.RunOnceAsync"/> (the same seam tests drive directly for
/// determinism). Never registered under the Testing environment.
/// </summary>
public sealed class LowStockNotificationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LowStockNotificationWorker> _log;

    public LowStockNotificationWorker(IServiceScopeFactory scopes, ILogger<LowStockNotificationWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                var count = await dispatcher.RunOnceAsync(stoppingToken);
                if (count > 0) _log.LogInformation("low-stock notifications dispatched {Count}", count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "low-stock notification pass failed; retrying next interval");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
