namespace Pos.Application.Ops;

/// <summary>One backup file on disk: when, how big, why it was taken, whether pg_restore could read it
/// (verified), and whether the off-machine copy succeeded.</summary>
public sealed record BackupRecord(
    string FileName,
    DateTimeOffset CreatedUtc,
    long SizeBytes,
    bool Verified,
    string Reason,
    bool OffsiteCopied,
    string? OffsiteError);

/// <summary>Backup health for the back-office dashboard — surfaces problems loudly, never silently.</summary>
public sealed record BackupHealth(
    BackupRecord? Last,
    bool Stale,                 // no successful backup within the staleness window (default 48h)
    bool OffsiteConfigured,
    bool OffsiteHealthy,
    string? SecondLocation,
    IReadOnlyList<string> Warnings);

public sealed record RestoreOutcome(bool Ok, string RestoredFrom, string? SafetyBackup, string? Error);

/// <summary>Backup/restore config (installer-set host config); the off-machine location is a DB Setting.</summary>
public sealed class BackupOptions
{
    public string ConnectionString { get; set; } = "";
    public string PgDumpPath { get; set; } = "pg_dump";
    public string BackupDirectory { get; set; } = "";
    public int RetentionDays { get; set; } = 14;
    public TimeOnly DailyTimeLocal { get; set; } = new(22, 30); // end-of-day default
    public int StaleHours { get; set; } = 48;
}

/// <summary>
/// Database backups using the bundled portable Postgres tools. pg_dump custom-format dumps are verified
/// (pg_restore --list), copied off-machine, and pruned by retention. Restore is destructive and always
/// takes a safety backup of the current state first.
/// </summary>
public interface IBackupService
{
    Task<BackupRecord> BackupNowAsync(string reason, CancellationToken ct = default);
    Task<IReadOnlyList<BackupRecord>> ListAsync(CancellationToken ct = default);
    Task<BackupHealth> GetHealthAsync(CancellationToken ct = default);
    Task<RestoreOutcome> RestoreAsync(string fileName, CancellationToken ct = default);
}
