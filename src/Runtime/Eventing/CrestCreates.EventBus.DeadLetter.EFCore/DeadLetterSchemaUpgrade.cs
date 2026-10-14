using Microsoft.EntityFrameworkCore;

namespace CrestCreates.EventBus.DeadLetter.EFCore;

/// <summary>
/// Additive, repeatable schema upgrade for the shared dead-letter table. The scripts only add the
/// nullable <c>LocalContextVersion</c> column: no DROP, no payload/key rewrite, no history
/// backfill. Verification is read-only and never issues DDL implicitly.
/// </summary>
public static class DeadLetterSchemaUpgrade
{
    public const string TableName = "DeadLetters";
    public const string LocalContextVersionColumn = "LocalContextVersion";

    /// <summary>Repeatable PostgreSQL upgrade script (safe to run twice).</summary>
    public const string PostgreSqlAddLocalContextVersionColumnSql =
        "ALTER TABLE \"" + TableName + "\" ADD COLUMN IF NOT EXISTS \"" + LocalContextVersionColumn + "\" integer NULL;";

    public static Task ApplyPostgreSqlAsync(DeadLetterDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.ExecuteSqlRawAsync(PostgreSqlAddLocalContextVersionColumnSql, ct);
    }

    public static async Task<bool> HasPostgreSqlLocalContextVersionColumnAsync(
        DeadLetterDbContext db,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var count = await db.Database
            .SqlQueryRaw<int>(
                $"SELECT COUNT(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = '{TableName}' AND column_name = '{LocalContextVersionColumn}'")
            .SingleAsync(ct);
        return count > 0;
    }
}
