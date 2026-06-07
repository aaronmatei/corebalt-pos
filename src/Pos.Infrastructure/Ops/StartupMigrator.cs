using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Ops;

/// <summary>
/// Safe startup auto-migration for an on-prem install: detect pending EF Core migrations and apply them,
/// but take a pre-migration backup FIRST whenever the database already holds client data. If the backup
/// fails, it REFUSES to migrate (throws) so the service start fails loudly rather than risking data. The
/// applied schema version is recorded to a file for remote support.
/// </summary>
public static class StartupMigrator
{
    public static async Task RunAsync(PosDbContext db, IDatabaseBackup backup, string schemaVersionFile,
        ILogger logger, CancellationToken ct = default)
    {
        var canConnect = await db.Database.CanConnectAsync(ct);
        List<string> applied, pending;
        if (canConnect)
        {
            applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
            pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        }
        else
        {
            // DB doesn't exist yet — don't query history (it would throw); every migration is pending and
            // Migrate() will create the database.
            applied = new();
            pending = db.Database.GetMigrations().ToList();
        }

        var plan = MigrationPlan.Decide(canConnect, applied, pending);
        logger.LogInformation("Migration check: {Reason}", plan.Reason);

        if (plan.BackupFirst)
        {
            logger.LogInformation("Taking a pre-migration backup before applying {Count} migration(s)…", pending.Count);
            var result = await backup.BackupAsync($"pre-{pending[0]}", ct);
            if (!result.Ok)
                throw new InvalidOperationException(
                    "Pre-migration backup FAILED — refusing to migrate so client data is never put at risk. " +
                    $"Fix the backup and restart. Reason: {result.Error}");
        }

        if (plan.Migrate)
        {
            await db.Database.MigrateAsync(ct);
            logger.LogInformation("Applied {Count} migration(s).", pending.Count);
        }

        RecordSchemaVersion(db, schemaVersionFile, pending.Count, logger);
    }

    private static void RecordSchemaVersion(PosDbContext db, string schemaVersionFile, int justApplied, ILogger logger)
    {
        try
        {
            var applied = db.Database.GetAppliedMigrations().ToList();
            var record = new
            {
                version = applied.LastOrDefault() ?? "(none)",
                appliedAtUtc = DateTimeOffset.UtcNow,
                migrationCount = applied.Count,
                justApplied,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(schemaVersionFile)!);
            File.WriteAllText(schemaVersionFile, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
            logger.LogInformation("Schema version: {Version} ({Count} migrations applied).", record.version, record.migrationCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not write the schema-version file (non-fatal).");
        }
    }
}
