using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Pos.Infrastructure.Ops;

public sealed record BackupResult(bool Ok, string? Path, string? Error)
{
    public static BackupResult Success(string path) => new(true, path, null);
    public static BackupResult Failure(string error) => new(false, null, error);
}

/// <summary>Takes a point-in-time database backup before a risky operation (e.g. a migration).</summary>
public interface IDatabaseBackup
{
    Task<BackupResult> BackupAsync(string label, CancellationToken ct = default);
}

/// <summary>
/// Backs up the local Postgres with <c>pg_dump</c> (custom format) to a timestamped file in the
/// per-install backups folder. Credentials/host/port come from the connection string; the password is
/// passed via PGPASSWORD (never on the command line). If pg_dump is missing or fails, returns a failure
/// so the caller can REFUSE to migrate rather than risk client data.
/// </summary>
public sealed class PgDumpBackup : IDatabaseBackup
{
    private readonly string _connectionString;
    private readonly string _backupDirectory;
    private readonly string _pgDumpPath;
    private readonly ILogger _log;
    private readonly TimeSpan _timeout;

    public PgDumpBackup(string connectionString, string backupDirectory, string pgDumpPath, ILogger log, TimeSpan? timeout = null)
    {
        _connectionString = connectionString;
        _backupDirectory = backupDirectory;
        _pgDumpPath = string.IsNullOrWhiteSpace(pgDumpPath) ? "pg_dump" : pgDumpPath;
        _log = log;
        _timeout = timeout ?? TimeSpan.FromMinutes(10);
    }

    public async Task<BackupResult> BackupAsync(string label, CancellationToken ct = default)
    {
        NpgsqlConnectionStringBuilder csb;
        try { csb = new NpgsqlConnectionStringBuilder(_connectionString); }
        catch (Exception ex) { return BackupResult.Failure($"Unreadable connection string: {ex.Message}"); }

        Directory.CreateDirectory(_backupDirectory);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var safeLabel = string.Concat((label ?? "backup").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        var file = Path.Combine(_backupDirectory, $"{csb.Database}-{stamp}-{safeLabel}.dump");

        var psi = new ProcessStartInfo(_pgDumpPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-h"); psi.ArgumentList.Add(csb.Host ?? "localhost");
        psi.ArgumentList.Add("-p"); psi.ArgumentList.Add((csb.Port == 0 ? 5432 : csb.Port).ToString());
        psi.ArgumentList.Add("-U"); psi.ArgumentList.Add(csb.Username ?? "postgres");
        psi.ArgumentList.Add("-d"); psi.ArgumentList.Add(csb.Database ?? "pos");
        psi.ArgumentList.Add("-F"); psi.ArgumentList.Add("c");      // custom format (compressed, restorable)
        psi.ArgumentList.Add("-w");                                  // never prompt — rely on PGPASSWORD
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(file);
        if (!string.IsNullOrEmpty(csb.Password)) psi.Environment["PGPASSWORD"] = csb.Password;

        Process process;
        try { process = Process.Start(psi)!; }
        catch (Win32Exception ex)
        {
            return BackupResult.Failure(
                $"pg_dump could not be launched ('{_pgDumpPath}'): {ex.Message}. Set Ops:PgDumpPath to the Postgres bin pg_dump.exe.");
        }
        catch (Exception ex) { return BackupResult.Failure($"pg_dump failed to start: {ex.Message}"); }

        using (process)
        {
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeout);
            try { await process.WaitForExitAsync(timeoutCts.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return BackupResult.Failure($"pg_dump timed out after {_timeout.TotalMinutes:0} minutes.");
            }

            var stderr = await stderrTask;
            if (process.ExitCode != 0)
                return BackupResult.Failure($"pg_dump exited {process.ExitCode}: {stderr.Trim()}");
            if (!File.Exists(file) || new FileInfo(file).Length == 0)
                return BackupResult.Failure("pg_dump reported success but the backup file is missing or empty.");

            _log.LogInformation("Database backup written: {File} ({Bytes} bytes).", file, new FileInfo(file).Length);
            return BackupResult.Success(file);
        }
    }
}
