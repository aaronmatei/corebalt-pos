using FluentAssertions;
using Pos.Infrastructure.Ops;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// The startup auto-migration SAFETY rule: never migrate a populated database without a pre-migration
/// backup; a new/empty database migrates straight through (nothing to lose).
/// </summary>
public sealed class MigrationPlanTests
{
    private static readonly string[] Some = { "20260101_A", "20260202_B" };
    private static readonly string[] None = System.Array.Empty<string>();

    [Fact]
    public void Up_to_date_database_does_not_migrate_and_does_not_back_up()
    {
        var plan = MigrationPlan.Decide(canConnect: true, applied: Some, pending: None);
        plan.Migrate.Should().BeFalse();
        plan.BackupFirst.Should().BeFalse();
    }

    [Fact]
    public void New_database_migrates_without_a_backup()
    {
        var plan = MigrationPlan.Decide(canConnect: false, applied: None, pending: Some);
        plan.Migrate.Should().BeTrue();
        plan.BackupFirst.Should().BeFalse("a database that doesn't exist yet has no data to protect");
    }

    [Fact]
    public void Empty_database_migrates_without_a_backup()
    {
        var plan = MigrationPlan.Decide(canConnect: true, applied: None, pending: Some);
        plan.Migrate.Should().BeTrue();
        plan.BackupFirst.Should().BeFalse("no applied migrations means no established client data");
    }

    [Fact]
    public void Populated_database_with_pending_migrations_requires_a_backup_first()
    {
        var plan = MigrationPlan.Decide(canConnect: true, applied: Some, pending: new[] { "20260303_C" });
        plan.Migrate.Should().BeTrue();
        plan.BackupFirst.Should().BeTrue("client data must be backed up before a schema change");
    }
}
