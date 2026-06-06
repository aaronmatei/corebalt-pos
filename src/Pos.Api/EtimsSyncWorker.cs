using Pos.Application.Fiscalization;

namespace Pos.Api;

/// <summary>
/// The seam for the real KRA batch upload: periodically transmits Signed-but-not-Synced sales. Thin
/// loop over <see cref="FiscalSyncService.RunOnceAsync"/>; the interval is the retry backoff. Only
/// registered when eTIMS is enabled (and never under the Testing environment, where tests drive the
/// sync service directly for determinism).
/// </summary>
public sealed class EtimsSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly EtimsOptions _options;
    private readonly ILogger<EtimsSyncWorker> _log;

    public EtimsSyncWorker(IServiceScopeFactory scopes, EtimsOptions options, ILogger<EtimsSyncWorker> log)
    {
        _scopes = scopes;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.SyncIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<FiscalSyncService>();
                var count = await sync.RunOnceAsync(stoppingToken);
                if (count > 0) _log.LogInformation("eTIMS sync transmitted {Count} sale(s) to KRA", count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "eTIMS sync pass failed; retrying next interval");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
