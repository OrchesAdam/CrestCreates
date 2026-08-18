using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlPendingEvidenceMigrationTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    /// <summary>
    /// C01: Reapplying all migrations twice must not drift schema.
    /// Each migration appears exactly once in history with a consistent checksum.
    /// </summary>
    [Fact]
    public async Task C01_ReapplyingMigration_Should_NotDriftSchema()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();

        // Schema already fully migrated by fixture. Re-apply should be a no-op.
        var runner = new PostgreSqlRuntimeMigrationRunner(lease.Options);
        await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });

        var history = await ReadHistoryAsync(lease.Options);
        history.Should().OnlyHaveUniqueItems(m => m.Version,
            "each migration version must appear exactly once after reapplication");
        history.Should().Contain(m => m.Version == "V011",
            "V011 control_plane_reference_data_stores must be present");

        // Validation-only after re-apply must succeed without error.
        await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
    }

    /// <summary>
    /// C02: Manually tampering a migration checksum in the history table
    /// must be detected by validation-only mode.
    /// </summary>
    [Fact]
    public async Task C02_MigrationValidation_Should_DetectChecksumDrift()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var runner = new PostgreSqlRuntimeMigrationRunner(lease.Options);

        // Tamper with V001 checksum in the history table.
        await using (var connection = new NpgsqlConnection(lease.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"update \"{lease.Options.Schema}\".crest_runtime_schema_migrations set checksum = 'tampered' where version = 'V001';",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        var act = async () => await runner.ApplyAsync(
            new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*incompatible*");
    }

    /// <summary>
    /// C03: Manually altering a V011 table column must be detected
    /// by validation-only mode as schema drift.
    /// </summary>
    [Fact]
    public async Task C03_MigrationValidation_Should_DetectSchemaDrift()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var runner = new PostgreSqlRuntimeMigrationRunner(lease.Options);

        // Drop a CHECK constraint from a V011 table to cause schema drift.
        await using (var connection = new NpgsqlConnection(lease.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"alter table \"{lease.Options.Schema}\".data_permission_scope_rules drop constraint ck_data_permission_scope_kind;",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        var act = async () => await runner.ApplyAsync(
            new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static async Task<(string Version, string Name, string Checksum)[]> ReadHistoryAsync(
        PostgreSqlRuntimePersistenceOptions options)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"select version, name, checksum from \"{options.Schema}\".crest_runtime_schema_migrations order by version collate \"C\";",
            connection);
        var result = new List<(string, string, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result.ToArray();
    }
}
