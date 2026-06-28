using Pos.Application.Sync;

namespace Pos.Api;

/// <summary>
/// On-prem background loop that ships this store's outbox to the HQ/cloud tier every interval. Registered
/// only in StoreServer mode when HqSync:Enabled. Each pass runs in its own DI scope; transport failures
/// are logged and retried next interval (nothing is acked until the cloud confirms).
/// </summary>
public sealed class HqSyncPushWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly HqSyncOptions _options;
    private readonly ILogger<HqSyncPushWorker> _log;

    public HqSyncPushWorker(IServiceScopeFactory scopes, HqSyncOptions options, ILogger<HqSyncPushWorker> log)
    {
        _scopes = scopes;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.IntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var pusher = scope.ServiceProvider.GetRequiredService<HqSyncPusher>();
                await pusher.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "hq.sync.push pass failed; retrying next interval");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
