using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pos.Application.Abstractions;
using Pos.Infrastructure.Ops;
using Pos.Infrastructure.Persistence;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Safe startup auto-migration: a populated database is backed up BEFORE a pending migration, and if the
/// backup fails the migration is REFUSED (nothing applied). New/up-to-date databases need no backup.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class StartupMigratorTests(PosApiFixture fx)
{
    private sealed class FakeBackup(bool ok) : IDatabaseBackup
    {
        public int Calls { get; private set; }
        public Task<BackupResult> BackupAsync(string label, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(ok ? BackupResult.Success("fake.dump") : BackupResult.Failure("simulated backup failure"));
        }
    }

    private PosDbContext NewContext(string database)
    {
        var cs = new NpgsqlConnectionStringBuilder(fx.ConnectionString) { Database = database }.ConnectionString;
        var opts = new DbContextOptionsBuilder<PosDbContext>()
            .UseNpgsql(cs, npg => npg.MigrationsHistoryTable("__ef_migrations")).Options;
        return new PosDbContext(opts, fx.Factory.Services.GetRequiredService<ISecretProtector>());
    }

    [Fact]
    public async Task A_failed_backup_refuses_to_migrate_a_populated_database()
    {
        var dbName = $"pos_ops_{Guid.NewGuid():N}";
        await using var db = NewContext(dbName);
        try
        {
            await db.Database.MigrateAsync(); // populate: full schema + applied-migrations history

            // Make the last migration look PENDING again on this populated DB (without re-running it).
            var last = (await db.Database.GetAppliedMigrationsAsync()).Last();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"__ef_migrations\" WHERE \"MigrationId\" = {0}", last);
            (await db.Database.GetPendingMigrationsAsync()).Should().Contain(last);

            var backup = new FakeBackup(ok: false);
            var act = async () => await StartupMigrator.RunAsync(db, backup,
                Path.Combine(Path.GetTempPath(), $"{dbName}.json"), NullLogger.Instance);

            await act.Should().ThrowAsync<InvalidOperationException>("a failed pre-migration backup must abort the migration");
            backup.Calls.Should().Be(1, "the backup is attempted before migrating");
            (await db.Database.GetPendingMigrationsAsync()).Should().Contain(last, "nothing was migrated");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task An_up_to_date_database_neither_backs_up_nor_migrates()
    {
        var dbName = $"pos_ops_{Guid.NewGuid():N}";
        await using var db = NewContext(dbName);
        try
        {
            await db.Database.MigrateAsync(); // fully up to date

            var backup = new FakeBackup(ok: false); // would fail if called — it must not be
            await StartupMigrator.RunAsync(db, backup, Path.Combine(Path.GetTempPath(), $"{dbName}.json"), NullLogger.Instance);

            backup.Calls.Should().Be(0, "no pending migrations → no backup");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task A_fresh_database_migrates_without_a_backup()
    {
        var dbName = $"pos_ops_{Guid.NewGuid():N}";
        await using var db = NewContext(dbName); // database does not exist yet
        try
        {
            var backup = new FakeBackup(ok: false); // must not be called for a brand-new DB
            await StartupMigrator.RunAsync(db, backup, Path.Combine(Path.GetTempPath(), $"{dbName}.json"), NullLogger.Instance);

            backup.Calls.Should().Be(0);
            (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty("the schema was created");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task PgDump_backup_reports_failure_when_pg_dump_is_missing()
    {
        var backup = new PgDumpBackup(fx.ConnectionString, Path.GetTempPath(),
            pgDumpPath: "corebalt-no-such-pgdump", log: NullLogger.Instance);

        var result = await backup.BackupAsync("test");

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("pg_dump");
    }
}
