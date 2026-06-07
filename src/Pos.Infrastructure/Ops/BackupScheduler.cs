using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pos.Application.Ops;

namespace Pos.Infrastructure.Ops;

/// <summary>
/// Runs the daily database backup at the configured local time (default end-of-day). A long-lived
/// background service; each run resolves a scoped <see cref="IBackupService"/> so it reads the current
/// off-machine location. Failures are logged, never thrown — the loop keeps going.
/// </summary>
public sealed class BackupScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly BackupOptions _opts;
    private readonly ILogger<BackupScheduler> _log;

    public BackupScheduler(IServiceScopeFactory scopes, BackupOptions opts, ILogger<BackupScheduler> log)
    {
        _scopes = scopes;
        _opts = opts;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Daily backup scheduled for {Time} local.", _opts.DailyTimeLocal);
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = _scopes.CreateScope();
                var backups = scope.ServiceProvider.GetRequiredService<IBackupService>();
                var record = await backups.BackupNowAsync("scheduled", stoppingToken);
                _log.LogInformation("Scheduled backup complete: {File} ({Bytes} bytes, verified={Verified}).",
                    record.FileName, record.SizeBytes, record.Verified);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Scheduled backup FAILED — will retry at the next scheduled time.");
            }
        }
    }

    private TimeSpan TimeUntilNextRun()
    {
        var now = DateTime.Now;
        var next = now.Date + _opts.DailyTimeLocal.ToTimeSpan();
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
