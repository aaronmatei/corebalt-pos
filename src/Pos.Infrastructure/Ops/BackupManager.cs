using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pos.Application.Identity;
using Pos.Application.Ops;
using Pos.Application.Tenancy;

namespace Pos.Infrastructure.Ops;

/// <summary>
/// Database backups/restore with the bundled portable Postgres tools. Dumps are pg_dump custom-format,
/// verified with `pg_restore --list`, copied to an off-machine location, and pruned by retention (the
/// pre-migration/safety dumps — anything labelled "pre-" — are kept). Restore is destructive: it always
/// takes a safety backup, closes app connections, then `pg_restore --clean`s the chosen file.
/// </summary>
public sealed class BackupManager : IBackupService
{
    private readonly BackupOptions _opts;
    private readonly IOpsSettingsRepository _ops;
    private readonly StoreServerOptions _store;
    private readonly ILogger<BackupManager> _log;
    private readonly NpgsqlConnectionStringBuilder _csb;

    public BackupManager(BackupOptions opts, IOpsSettingsRepository ops, StoreServerOptions store, ILogger<BackupManager> log)
    {
        _opts = opts;
        _ops = ops;
        _store = store;
        _log = log;
        _csb = new NpgsqlConnectionStringBuilder(opts.ConnectionString);
    }

    private string PgRestorePath
    {
        get
        {
            var dir = Path.GetDirectoryName(_opts.PgDumpPath);
            var ext = Path.GetExtension(_opts.PgDumpPath);
            return string.IsNullOrEmpty(dir) ? "pg_restore" + ext : Path.Combine(dir, "pg_restore" + ext);
        }
    }

    public async Task<BackupRecord> BackupNowAsync(string reason, CancellationToken ct = default)
    {
        // 1. dump (reuses the part-1 pg_dump wrapper) ------------------------------------------------
        var dumper = new PgDumpBackup(_opts.ConnectionString, _opts.BackupDirectory, _opts.PgDumpPath, _log);
        var dump = await dumper.BackupAsync(reason, ct);
        if (!dump.Ok) throw new InvalidOperationException($"Backup failed: {dump.Error}");
        var file = dump.Path!;

        // 2. integrity check: pg_restore --list must read it ----------------------------------------
        var verified = await VerifyAsync(file, ct);
        if (!verified) _log.LogWarning("Backup {File} could not be verified by pg_restore --list.", file);

        // 3. off-machine copy -----------------------------------------------------------------------
        var (offsiteOk, offsiteErr) = await CopyOffsiteAsync(file, ct);

        WriteSidecar(file, reason, verified, offsiteOk, offsiteErr);

        // 4. retention prune (keep "pre-" safety/pre-migration dumps) --------------------------------
        Prune();

        return ToRecord(file);
    }

    public Task<IReadOnlyList<BackupRecord>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_opts.BackupDirectory))
            return Task.FromResult<IReadOnlyList<BackupRecord>>(Array.Empty<BackupRecord>());
        var records = Directory.GetFiles(_opts.BackupDirectory, "*.dump")
            .Select(ToRecord)
            .OrderByDescending(r => r.CreatedUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<BackupRecord>>(records);
    }

    public async Task<BackupHealth> GetHealthAsync(CancellationToken ct = default)
    {
        var records = await ListAsync(ct);
        var last = records.FirstOrDefault(r => r.Verified) ?? records.FirstOrDefault();
        var stale = last is null || (DateTimeOffset.UtcNow - last.CreatedUtc).TotalHours > _opts.StaleHours;

        var secondLocation = await GetSecondLocationAsync(ct);
        var offsiteConfigured = !string.IsNullOrWhiteSpace(secondLocation);
        var offsiteHealthy = offsiteConfigured && last is { OffsiteCopied: true };

        var warnings = new List<string>();
        if (stale) warnings.Add($"No successful backup in the last {_opts.StaleHours} hours.");
        if (!offsiteConfigured) warnings.Add("Off-machine backup location is not set — backups exist only on this machine.");
        else if (last is { OffsiteCopied: false }) warnings.Add("The off-machine backup copy is failing.");

        return new BackupHealth(last, stale, offsiteConfigured, offsiteHealthy, secondLocation, warnings);
    }

    public async Task<RestoreOutcome> RestoreAsync(string fileName, CancellationToken ct = default)
    {
        var file = Path.Combine(_opts.BackupDirectory, Path.GetFileName(fileName));
        if (!File.Exists(file))
        {
            // Fall back to the off-machine copy and stage it locally.
            var secondLocation = await GetSecondLocationAsync(ct);
            var offsite = string.IsNullOrWhiteSpace(secondLocation) ? null : Path.Combine(secondLocation, Path.GetFileName(fileName));
            if (offsite is not null && File.Exists(offsite)) { Directory.CreateDirectory(_opts.BackupDirectory); File.Copy(offsite, file, true); }
            else return new RestoreOutcome(false, fileName, null, "Backup file not found locally or off-machine.");
        }

        // 1. SAFETY backup of the CURRENT state — never restore without it.
        BackupRecord safety;
        try { safety = await BackupNowAsync("pre-restore-safety", ct); }
        catch (Exception ex) { return new RestoreOutcome(false, fileName, null, $"Refused to restore — safety backup failed: {ex.Message}"); }

        // 2. close app connections (clear our pool + terminate other backends on the DB).
        await TerminateConnectionsAsync(ct);
        NpgsqlConnection.ClearAllPools();

        // 3. restore (drop + recreate objects, then load).
        var (exit, stderr) = await RunAsync(PgRestorePath, new[]
        {
            "--clean", "--if-exists", "--no-owner",
            "-h", _csb.Host ?? "localhost", "-p", Port().ToString(), "-U", _csb.Username ?? "postgres",
            "-d", _csb.Database ?? "pos", "-w", file
        }, ct);

        // pg_restore can emit non-fatal warnings (e.g. DROP of a not-yet-existing object) on exit 1; treat a
        // clean exit as success and surface stderr otherwise.
        if (exit != 0)
            return new RestoreOutcome(false, fileName, safety.FileName, $"pg_restore exited {exit}: {stderr.Trim()}");

        _log.LogWarning("Database RESTORED from {File} (safety backup: {Safety}).", fileName, safety.FileName);
        return new RestoreOutcome(true, fileName, safety.FileName, null);
    }

    // ── internals ────────────────────────────────────────────────────────────────────────────────
    private int Port() => _csb.Port == 0 ? 5432 : _csb.Port;

    private async Task<bool> VerifyAsync(string file, CancellationToken ct)
    {
        var (exit, _) = await RunAsync(PgRestorePath, new[] { "--list", file }, ct);
        return exit == 0;
    }

    private async Task<(bool ok, string? error)> CopyOffsiteAsync(string file, CancellationToken ct)
    {
        var dest = await GetSecondLocationAsync(ct);
        if (string.IsNullOrWhiteSpace(dest)) return (false, null); // not configured (surfaced by health)
        try
        {
            Directory.CreateDirectory(dest);
            var target = Path.Combine(dest, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
            var sidecar = file + ".meta.json";
            if (File.Exists(sidecar)) File.Copy(sidecar, target + ".meta.json", overwrite: true);
            return (true, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Off-machine backup copy to {Dest} failed.", dest);
            return (false, ex.Message);
        }
    }

    private async Task TerminateConnectionsAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_opts.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @d AND pid <> pg_backend_pid()";
            cmd.Parameters.AddWithValue("@d", _csb.Database ?? "pos");
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not terminate existing DB connections before restore."); }
    }

    private async Task<string?> GetSecondLocationAsync(CancellationToken ct)
    {
        try { return (await _ops.GetAsync(_store.TenantId, ct))?.SecondBackupLocation; }
        catch { return null; }
    }

    private void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_opts.RetentionDays);
        foreach (var file in Directory.GetFiles(_opts.BackupDirectory, "*.dump"))
        {
            var name = Path.GetFileName(file);
            if (name.Contains("-pre-", StringComparison.OrdinalIgnoreCase)) continue; // keep pre-migration/safety dumps
            if (CreatedUtcOf(file) >= cutoff) continue;
            try
            {
                File.Delete(file);
                var sidecar = file + ".meta.json";
                if (File.Exists(sidecar)) File.Delete(sidecar);
                _log.LogInformation("Pruned old backup {File}.", name);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Could not prune {File}.", name); }
        }
    }

    private void WriteSidecar(string file, string reason, bool verified, bool offsiteOk, string? offsiteErr)
    {
        try
        {
            var meta = new { CreatedUtc = DateTimeOffset.UtcNow, SizeBytes = new FileInfo(file).Length, Reason = reason, Verified = verified, OffsiteCopied = offsiteOk, OffsiteError = offsiteErr };
            File.WriteAllText(file + ".meta.json", JsonSerializer.Serialize(meta));
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not write backup sidecar for {File}.", file); }
    }

    private static DateTimeOffset CreatedUtcOf(string file) => new FileInfo(file).LastWriteTimeUtc;

    private static BackupRecord ToRecord(string file)
    {
        var info = new FileInfo(file);
        var sidecar = file + ".meta.json";
        if (File.Exists(sidecar))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(sidecar));
                var r = doc.RootElement;
                return new BackupRecord(
                    info.Name,
                    r.TryGetProperty("CreatedUtc", out var c) ? c.GetDateTimeOffset() : info.LastWriteTimeUtc,
                    r.TryGetProperty("SizeBytes", out var s) ? s.GetInt64() : info.Length,
                    r.TryGetProperty("Verified", out var v) && v.GetBoolean(),
                    r.TryGetProperty("Reason", out var rs) ? (rs.GetString() ?? "") : "",
                    r.TryGetProperty("OffsiteCopied", out var o) && o.GetBoolean(),
                    r.TryGetProperty("OffsiteError", out var e) ? e.GetString() : null);
            }
            catch { /* unreadable sidecar — fall through to filesystem-only */ }
        }
        return new BackupRecord(info.Name, info.LastWriteTimeUtc, info.Length, Verified: false, Reason: ReasonFromName(info.Name), OffsiteCopied: false, OffsiteError: null);
    }

    private static string ReasonFromName(string name)
    {
        // {db}-{yyyyMMdd}-{HHmmss}-{reason}.dump
        var stem = Path.GetFileNameWithoutExtension(name);
        var parts = stem.Split('-');
        return parts.Length >= 4 ? string.Join("-", parts[3..]) : "(unknown)";
    }

    private async Task<(int exit, string stderr)> RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (!string.IsNullOrEmpty(_csb.Password)) psi.Environment["PGPASSWORD"] = _csb.Password;

        Process process;
        try { process = Process.Start(psi)!; }
        catch (Win32Exception ex) { return (-1, $"Could not launch '{exe}': {ex.Message}"); }

        using (process)
        {
            // Drain BOTH pipes: pg_restore --list writes its table-of-contents to stdout; if we don't read
            // it the pipe buffer fills and the process blocks forever.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { process.Kill(true); } catch { } return (-1, "timed out"); }
            await stdoutTask;
            return (process.ExitCode, await stderrTask);
        }
    }
}
