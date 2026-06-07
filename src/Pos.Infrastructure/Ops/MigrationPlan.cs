namespace Pos.Infrastructure.Ops;

/// <summary>
/// The safety decision for startup auto-migration: WHETHER to migrate and whether a pre-migration
/// backup is required FIRST. The rule that protects client data: never apply a pending migration to a
/// populated database without a backup. A brand-new or empty database has nothing to lose, so it
/// migrates straight through.
/// </summary>
public sealed record MigrationPlan(bool Migrate, bool BackupFirst, string Reason)
{
    public static MigrationPlan Decide(bool canConnect, IReadOnlyList<string> applied, IReadOnlyList<string> pending)
    {
        if (pending.Count == 0)
            return new(Migrate: false, BackupFirst: false, "Database schema is up to date — nothing to migrate.");

        if (!canConnect)
            return new(Migrate: true, BackupFirst: false,
                "Database does not exist yet — applying the initial schema (no data to back up).");

        if (applied.Count == 0)
            return new(Migrate: true, BackupFirst: false,
                "Database is empty (no applied migrations) — applying the schema (no data to back up).");

        return new(Migrate: true, BackupFirst: true,
            $"{pending.Count} pending migration(s) on a populated database — a pre-migration backup is required first.");
    }
}
