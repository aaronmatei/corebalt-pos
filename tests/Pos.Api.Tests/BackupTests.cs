using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pos.Application.Abstractions;
using Pos.Application.Identity;
using Pos.Application.Ops;
using Pos.Application.Tenancy;
using Pos.Domain.Tenancy;
using Pos.Infrastructure.Ops;
using Pos.Infrastructure.Persistence;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Backup + restore against a real Postgres using the portable pg_dump/pg_restore. Verified dumps,
/// retention pruning (pre- dumps kept), off-machine copy, health warnings, and a guarded restore that
/// takes a safety backup first and brings the data back.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class BackupTests(PosApiFixture fx)
{
    private sealed class FakeOps(string? second, Guid tenant) : IOpsSettingsRepository
    {
        private readonly OpsSettings? _s = Build(second, tenant);
        private static OpsSettings? Build(string? second, Guid tenant)
        {
            if (string.IsNullOrEmpty(second)) return null;
            var o = OpsSettings.Create(tenant); o.SetSecondBackupLocation(second); return o;
        }
        public Task<OpsSettings?> GetAsync(Guid t, CancellationToken ct = default) => Task.FromResult(_s);
        public Task AddAsync(OpsSettings s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static string PgDumpPath()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL");
        // Newest installed pg_dump that can dump the server (>= its version).
        var found = Directory.Exists(root)
            ? Directory.GetFiles(root, "pg_dump.exe", SearchOption.AllDirectories).OrderBy(p => p).LastOrDefault()
            : null;
        return found ?? "pg_dump";
    }

    private (PosDbContext db, string conn, string dbName) NewScratchDb()
    {
        var dbName = $"pos_bk_{Guid.NewGuid():N}";
        var conn = new NpgsqlConnectionStringBuilder(fx.ConnectionString) { Database = dbName }.ConnectionString;
        var opts = new DbContextOptionsBuilder<PosDbContext>()
            .UseNpgsql(conn, npg => npg.MigrationsHistoryTable("__ef_migrations")).Options;
        return (new PosDbContext(opts, fx.Factory.Services.GetRequiredService<ISecretProtector>()), conn, dbName);
    }

    private BackupManager Manager(string conn, string backupDir, string? offsite = null)
    {
        var opts = new BackupOptions { ConnectionString = conn, PgDumpPath = PgDumpPath(), BackupDirectory = backupDir, RetentionDays = 14 };
        var store = new StoreServerOptions { TenantId = Guid.NewGuid(), StoreId = Guid.NewGuid() };
        return new BackupManager(opts, new FakeOps(offsite, store.TenantId), store, NullLogger<BackupManager>.Instance);
    }

    private static string Temp() { var d = Path.Combine(Path.GetTempPath(), $"posbk-{Guid.NewGuid():N}"); Directory.CreateDirectory(d); return d; }

    [Fact]
    public async Task Backup_produces_a_verified_dump_and_copies_off_machine()
    {
        var (db, conn, _) = NewScratchDb();
        var backupDir = Temp(); var offsite = Temp();
        try
        {
            await db.Database.MigrateAsync();
            var record = await Manager(conn, backupDir, offsite).BackupNowAsync("manual");

            record.Verified.Should().BeTrue("pg_restore --list could read it");
            record.OffsiteCopied.Should().BeTrue();
            File.Exists(Path.Combine(backupDir, record.FileName)).Should().BeTrue();
            File.Exists(Path.Combine(offsite, record.FileName)).Should().BeTrue("the off-machine copy landed");
            record.SizeBytes.Should().BeGreaterThan(0);
        }
        finally { await db.Database.EnsureDeletedAsync(); Directory.Delete(backupDir, true); Directory.Delete(offsite, true); }
    }

    [Fact]
    public async Task Retention_prunes_old_backups_but_keeps_pre_migration_dumps()
    {
        var (db, conn, _) = NewScratchDb();
        var backupDir = Temp();
        try
        {
            await db.Database.MigrateAsync();
            // Two stale files: a routine one (should be pruned) and a pre-migration one (must be kept).
            var oldScheduled = Path.Combine(backupDir, "pos-20200101-000000-scheduled.dump");
            var oldPre = Path.Combine(backupDir, "pos-20200101-000000-pre-Init.dump");
            File.WriteAllText(oldScheduled, "x"); File.WriteAllText(oldPre, "x");
            var stale = DateTime.UtcNow.AddDays(-30);
            File.SetLastWriteTimeUtc(oldScheduled, stale); File.SetLastWriteTimeUtc(oldPre, stale);

            await Manager(conn, backupDir).BackupNowAsync("manual"); // triggers prune

            File.Exists(oldScheduled).Should().BeFalse("a routine backup older than retention is pruned");
            File.Exists(oldPre).Should().BeTrue("pre-migration/safety backups are kept");
        }
        finally { await db.Database.EnsureDeletedAsync(); Directory.Delete(backupDir, true); }
    }

    [Fact]
    public async Task Restore_takes_a_safety_backup_first_and_brings_the_data_back()
    {
        var (db, conn, _) = NewScratchDb();
        var backupDir = Temp();
        try
        {
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO registers (id, tenant_id, store_id, number, name) VALUES (gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), '1', 'Lane 1')");

            var mgr = Manager(conn, backupDir);
            var backup = await mgr.BackupNowAsync("manual");

            // Lose the data, then restore.
            await db.Database.ExecuteSqlRawAsync("DELETE FROM registers");
            (await CountRegistersAsync(conn)).Should().Be(0);

            var outcome = await mgr.RestoreAsync(backup.FileName);

            outcome.Ok.Should().BeTrue(outcome.Error ?? "");
            outcome.SafetyBackup.Should().NotBeNullOrEmpty();
            File.Exists(Path.Combine(backupDir, outcome.SafetyBackup!)).Should().BeTrue("a safety backup of current state was taken first");
            outcome.SafetyBackup.Should().Contain("pre-restore-safety");
            (await CountRegistersAsync(conn)).Should().Be(1, "the restore brought the row back");
        }
        finally { await db.Database.EnsureDeletedAsync(); Directory.Delete(backupDir, true); }
    }

    [Fact]
    public async Task Health_warns_when_no_backup_exists_and_when_offsite_is_unset()
    {
        var (db, conn, _) = NewScratchDb();
        var backupDir = Temp();
        try
        {
            var health = await Manager(conn, backupDir, offsite: null).GetHealthAsync(); // empty dir, no offsite
            health.Stale.Should().BeTrue();
            health.OffsiteConfigured.Should().BeFalse();
            health.Warnings.Should().Contain(w => w.Contains("No successful backup"));
            health.Warnings.Should().Contain(w => w.Contains("Off-machine"));
        }
        finally { Directory.Delete(backupDir, true); }
    }

    private static async Task<long> CountRegistersAsync(string conn)
    {
        await using var c = new NpgsqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM registers";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
