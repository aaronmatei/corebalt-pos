using Pos.Application.Integration;

namespace Pos.Api;

/// <summary>
/// Forwards completed sales to the Corebalt ERP on an interval. Thin loop over
/// <see cref="IErpSaleForwarder.RunOnceAsync"/> (the same seam tests drive directly); the interval is
/// the retry backoff. Only registered when CorebaltErp is enabled (and never under Testing).
/// </summary>
public sealed class CorebaltSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly CorebaltErpOptions _options;
    private readonly ILogger<CorebaltSyncWorker> _log;

    public CorebaltSyncWorker(IServiceScopeFactory scopes, CorebaltErpOptions options,
        ILogger<CorebaltSyncWorker> log)
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
                var forwarder = scope.ServiceProvider.GetRequiredService<IErpSaleForwarder>();
                var count = await forwarder.RunOnceAsync(stoppingToken);
                if (count > 0) _log.LogInformation("forwarded {Count} sale(s) to Corebalt ERP", count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Corebalt ERP sync pass failed; retrying next interval");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
