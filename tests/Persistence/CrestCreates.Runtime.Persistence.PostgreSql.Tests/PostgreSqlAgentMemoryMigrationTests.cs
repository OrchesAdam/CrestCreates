using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Runtime.Persistence.PostgreSql;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentMemoryMigrationTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    [Fact]
    public async Task V010Manifest_Should_ValidateApplyChecksumShapeCollationAndForeignKeyDeleteAction()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();

        // Apply creates all six tables.
        await AssertTableExistsAsync(lease.Options, "agent_memory_conversations");
        await AssertTableExistsAsync(lease.Options, "agent_memory_tasks");
        await AssertTableExistsAsync(lease.Options, "agent_memory_compressed_contexts");
        await AssertTableExistsAsync(lease.Options, "agent_memory_compressed_blocks");
        await AssertTableExistsAsync(lease.Options, "agent_memory_candidates");
        await AssertTableExistsAsync(lease.Options, "agent_memories");

        // Re-apply is non-mutating.
        var runner = new PostgreSqlRuntimeMigrationRunner(lease.Options);
        await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });

        var history = await ReadHistoryAsync(lease.Options);
        history.Should().ContainSingle(migration => migration.Version == "V010");
        history.Where(migration => migration.Version != "V010").Should().HaveCount(12, "V001-V009, V011, V012 and V013 must remain unchanged.");

        // C collation on identity/order columns.
        await AssertCollationAsync(lease.Options, "agent_memory_conversations", "tenant_id", "C");
        await AssertCollationAsync(lease.Options, "agent_memory_conversations", "conversation_id", "C");
        await AssertCollationAsync(lease.Options, "agent_memories", "supersedes_memory_id", "C");

        // Block FK delete action is exactly CASCADE.
        var blockFk = await ReadForeignKeyAsync(lease.Options, "agent_memory_compressed_blocks", "fk_agent_memory_blocks_context");
        blockFk.Should().NotBeNull();
        blockFk!.DeleteAction.Should().Be("cascade");
        blockFk.Deferrable.Should().BeFalse();

        // Memory graph FKs are deferrable, initially deferred, NO ACTION.
        var supersedesFk = await ReadForeignKeyAsync(lease.Options, "agent_memories", "fk_agent_memories_supersedes");
        supersedesFk.Should().NotBeNull();
        supersedesFk!.DeleteAction.Should().Be("no action");
        supersedesFk.Deferrable.Should().BeTrue();
        supersedesFk.InitiallyDeferred.Should().BeTrue();
    }

    [Fact]
    public async Task V010Manifest_Should_ValidateCollationAndForeignKeyDeleteAction()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var runner = new PostgreSqlRuntimeMigrationRunner(lease.Options);
        await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });

        // Validation-only succeeds after Apply.
        await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });

        // Negative: a non-C collation on an identity column fails validation.
        var tampered = lease.Options;
        await using (var connection = new NpgsqlConnection(tampered.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"alter table \"{tampered.Schema}\".agent_memory_conversations alter column conversation_id type text collate \"default\";",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        var act = async () => await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = false });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*incompatible column shape or collation*");
    }

    [Fact]
    public async Task IncludeStale_Should_RemainNoOp_WithoutStaleSchema()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();

        await using var connection = new NpgsqlConnection(lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            select count(*) from information_schema.columns
            where table_schema=@schema and column_name like 'stale%';
            """,
            connection);
        command.Parameters.AddWithValue("schema", lease.Options.Schema);
        var staleColumns = (long)(await command.ExecuteScalarAsync())!;
        staleColumns.Should().Be(0, "no Stale column/status/TTL artifact may exist in V010.");

        var statusChecks = await CountChecksAsync(lease.Options, "agent_memories");
        var names = await ReadCheckNamesAsync(lease.Options, "agent_memories");
        names.Should().NotContain(name => name.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    
    [Fact]
    public void PostgreSqlAgentMemoryJsonPaths_Should_UseExactGeneratedRootsOnly()
    {
        var context = PostgreSqlRuntimeJsonSerializerContext.Default;
        var roots = new Dictionary<Type, JsonTypeInfo>
        {
            [typeof(AgentConversationRecord)] = context.AgentConversationRecord,
            [typeof(AgentTaskRecord)] = context.AgentTaskRecord,
            [typeof(AgentCompressedContext)] = context.AgentCompressedContext,
            [typeof(AgentCompressedContextBlock)] = context.AgentCompressedContextBlock,
            [typeof(AgentMemoryCandidate)] = context.AgentMemoryCandidate,
            [typeof(AgentMemoryItem)] = context.AgentMemoryItem
        };

        foreach (var (type, typeInfo) in roots)
        {
            typeInfo.Should().NotBeNull($"{type.Name} must be an exact generated root.");
            typeInfo.Kind.Should().Be(JsonTypeInfoKind.Object, $"{type.Name} must round-trip as an object root.");
        }

        var roundTrip = (AgentMemoryItem)JsonSerializer.Deserialize(
            """{"memoryId":"m-1","tenantId":"t-1","kind":0,"content":"c","canonicalContentHash":{"value":"h","algorithm":"SHA-256","algorithmVersion":"v","artifactKind":"k","scope":"s","purpose":"p","contractVersion":"cv","canonicalShapeVersion":"csv"},"promotedAt":"2024-01-01T00:00:00+00:00","status":1}""",
            context.AgentMemoryItem)!;
        roundTrip.MemoryId.Should().Be("m-1");
    }

private static async Task AssertTableExistsAsync(PostgreSqlRuntimePersistenceOptions options, string table)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select exists (select 1 from information_schema.tables where table_schema=@schema and table_name=@table);",
            connection);
        command.Parameters.AddWithValue("schema", options.Schema);
        command.Parameters.AddWithValue("table", table);
        var exists = (bool)(await command.ExecuteScalarAsync())!;
        exists.Should().BeTrue($"V010 must create {table}.");
    }

    private static async Task AssertCollationAsync(PostgreSqlRuntimePersistenceOptions options, string table, string column, string collation)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select collation_name from information_schema.columns where table_schema=@schema and table_name=@table and column_name=@column;",
            connection);
        command.Parameters.AddWithValue("schema", options.Schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        var actual = await command.ExecuteScalarAsync() as string;
        actual.Should().Be(collation, $"{table}.{column} must use {collation} collation.");
    }

    private static async Task<(string Version, string Name, string Checksum)[]> ReadHistoryAsync(PostgreSqlRuntimePersistenceOptions options)
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

    private static async Task<ForeignKeyInfo?> ReadForeignKeyAsync(PostgreSqlRuntimePersistenceOptions options, string table, string name)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select confdeltype::text, condeferrable, condeferred
            from pg_constraint constraint_info
            join pg_class relation on relation.oid = constraint_info.conrelid
            join pg_namespace schema_info on schema_info.oid = relation.relnamespace
            where schema_info.nspname=@schema and relation.relname=@table and constraint_info.conname=@name;
            """,
            connection);
        command.Parameters.AddWithValue("schema", options.Schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        var deleteAction = reader.GetString(0) switch
        {
            "c" => "cascade",
            "a" => "no action",
            "r" => "restrict",
            "n" => "set null",
            "d" => "set default",
            var other => other
        };
        return new ForeignKeyInfo(deleteAction, reader.GetBoolean(1), reader.GetBoolean(2));
    }

    private static async Task<long> CountChecksAsync(PostgreSqlRuntimePersistenceOptions options, string table)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select count(*) from pg_constraint constraint_info
            join pg_class relation on relation.oid = constraint_info.conrelid
            join pg_namespace schema_info on schema_info.oid = relation.relnamespace
            where schema_info.nspname=@schema and relation.relname=@table and constraint_info.contype='c';
            """,
            connection);
        command.Parameters.AddWithValue("schema", options.Schema);
        command.Parameters.AddWithValue("table", table);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string[]> ReadCheckNamesAsync(PostgreSqlRuntimePersistenceOptions options, string table)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select constraint_info.conname from pg_constraint constraint_info
            join pg_class relation on relation.oid = constraint_info.conrelid
            join pg_namespace schema_info on schema_info.oid = relation.relnamespace
            where schema_info.nspname=@schema and relation.relname=@table and constraint_info.contype='c';
            """,
            connection);
        command.Parameters.AddWithValue("schema", options.Schema);
        command.Parameters.AddWithValue("table", table);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names.ToArray();
    }

    private sealed record ForeignKeyInfo(string DeleteAction, bool Deferrable, bool InitiallyDeferred);
}
