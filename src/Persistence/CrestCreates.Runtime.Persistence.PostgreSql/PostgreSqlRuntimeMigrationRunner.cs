using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Applies an immutable, checksummed migration catalog. Validation-only mode
/// executes no DDL and refuses a database which is not exactly compatible.
/// </summary>
public sealed class PostgreSqlRuntimeMigrationRunner
{
    private const string HistoryTable = "crest_runtime_schema_migrations";
    private readonly PostgreSqlRuntimePersistenceOptions _options;

    public PostgreSqlRuntimeMigrationRunner(PostgreSqlRuntimePersistenceOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task ApplyAsync(PostgreSqlRuntimeMigrationOptions migrationOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migrationOptions);
        PostgreSqlRuntimePersistenceOptionsValidator.Validate(_options);
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!migrationOptions.ApplyMigrations)
        {
            await ValidateOnlyAsync(connection, cancellationToken).ConfigureAwait(false);
            return;
        }

        var lockKey = $"crest-runtime-migrations:{_options.Schema}";
        await ExecuteAsync(connection, "select pg_advisory_lock(hashtext(@key));", [new("key", lockKey)], cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureBootstrapAsync(connection, cancellationToken).ConfigureAwait(false);
            await ValidateHistoryTableShapeAsync(connection, cancellationToken).ConfigureAwait(false);
            var applied = await ReadHistoryAsync(connection, cancellationToken).ConfigureAwait(false);
            ValidateHistory(applied, allowPending: true);
            foreach (var migration in Catalog.Skip(applied.Count))
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await ExecuteAsync(connection, migration.Sql.Replace("{schema}", QuotedSchema(), StringComparison.Ordinal), [], cancellationToken, transaction).ConfigureAwait(false);
                await ExecuteAsync(connection,
                    $"insert into {Qualified(HistoryTable)} (version, name, checksum) values (@version, @name, @checksum);",
                    [new("version", migration.Version), new("name", migration.Name), new("checksum", migration.Checksum)],
                    cancellationToken,
                    transaction).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            var finalHistory = await ReadHistoryAsync(connection, cancellationToken).ConfigureAwait(false);
            ValidateHistory(finalHistory, allowPending: false);
            await ValidateRequiredTablesAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await ExecuteAsync(connection, "select pg_advisory_unlock(hashtext(@key));", [new("key", lockKey)], CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task ValidateOnlyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var exists = await ScalarAsync<bool>(connection,
            "select exists (select 1 from information_schema.schemata where schema_name=@schema) and exists (select 1 from information_schema.tables where table_schema=@schema and table_name=@history);",
            [new("schema", _options.Schema), new("history", HistoryTable)],
            cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            throw new InvalidOperationException(
                "PostgreSQL runtime schema/history is missing; validation-only mode executes no DDL.");
        }

        await ValidateHistoryTableShapeAsync(connection, cancellationToken).ConfigureAwait(false);
        var history = await ReadHistoryAsync(connection, cancellationToken).ConfigureAwait(false);
        ValidateHistory(history, allowPending: false);
        await ValidateRequiredTablesAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureBootstrapAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, $"create schema if not exists {QuotedSchema()};", [], cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection,
            $"create table if not exists {Qualified(HistoryTable)} (version text primary key, name text not null, checksum text not null, applied_at timestamptz not null default clock_timestamp());",
            [], cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<AppliedMigration>> ReadHistoryAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select version, name, checksum from {Qualified(HistoryTable)} order by version collate \"C\";", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<AppliedMigration>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new AppliedMigration(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    private async Task ValidateHistoryTableShapeAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var columns = new Dictionary<string, (string Type, string Nullable)>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(
            "select column_name, data_type, is_nullable from information_schema.columns where table_schema=@schema and table_name=@table order by ordinal_position;",
            connection))
        {
            command.Parameters.AddWithValue("schema", _options.Schema);
            command.Parameters.AddWithValue("table", HistoryTable);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                columns.Add(reader.GetString(0), (reader.GetString(1), reader.GetString(2)));
        }

        var expected = new Dictionary<string, (string Type, string Nullable)>(StringComparer.Ordinal)
        {
            ["version"] = ("text", "NO"),
            ["name"] = ("text", "NO"),
            ["checksum"] = ("text", "NO"),
            ["applied_at"] = ("timestamp with time zone", "NO")
        };
        if (columns.Count != expected.Count || expected.Any(pair => !columns.TryGetValue(pair.Key, out var actual) || actual != pair.Value))
            throw new InvalidOperationException("PostgreSQL runtime migration history table has an incompatible shape.");

        var primaryKeyColumns = new List<string>();
        await using (var command = new NpgsqlCommand(
            """
            select key_column.column_name
            from information_schema.table_constraints constraint_info
            join information_schema.key_column_usage key_column
              on key_column.constraint_name = constraint_info.constraint_name
             and key_column.table_schema = constraint_info.table_schema
             and key_column.table_name = constraint_info.table_name
            where constraint_info.table_schema=@schema
              and constraint_info.table_name=@table
              and constraint_info.constraint_type='PRIMARY KEY'
            order by key_column.ordinal_position;
            """,
            connection))
        {
            command.Parameters.AddWithValue("schema", _options.Schema);
            command.Parameters.AddWithValue("table", HistoryTable);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                primaryKeyColumns.Add(reader.GetString(0));
        }

        if (primaryKeyColumns.Count != 1 || !string.Equals(primaryKeyColumns[0], "version", StringComparison.Ordinal))
            throw new InvalidOperationException("PostgreSQL runtime migration history table must have a primary key on version.");
    }

    private void ValidateHistory(IReadOnlyList<AppliedMigration> applied, bool allowPending)
    {
        if (applied.Count > Catalog.Count)
            throw new InvalidOperationException("PostgreSQL runtime database contains a newer unknown migration.");
        for (var index = 0; index < applied.Count; index++)
        {
            var expected = Catalog[index];
            var actual = applied[index];
            if (!string.Equals(actual.Version, expected.Version, StringComparison.Ordinal)
                || !string.Equals(actual.Name, expected.Name, StringComparison.Ordinal)
                || !string.Equals(actual.Checksum, expected.Checksum, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"PostgreSQL runtime migration history is incompatible at '{actual.Version}'.");
            }
        }
        if (!allowPending && applied.Count != Catalog.Count)
            throw new InvalidOperationException("PostgreSQL runtime database has pending migrations in validation-only mode.");
    }

    private async Task ValidateRequiredTablesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var expected = new[]
        {
            "runtime_workflow_instances",
            "runtime_human_task_instances",
            "runtime_operation_receipts",
            "descriptor_snapshots",
            "descriptor_snapshot_entries",
            "runtime_audit_envelopes"
        };
        var count = await ScalarAsync<long>(connection,
            "select count(*) from information_schema.tables where table_schema=@schema and table_name = any(@tables);",
            [new("schema", _options.Schema), new("tables", expected)],
            cancellationToken).ConfigureAwait(false);
        if (count != expected.Length)
            throw new InvalidOperationException("PostgreSQL runtime schema has an unexpected or incomplete table shape.");
    }

    private string QuotedSchema() => $"\"{_options.Schema}\"";
    private string Qualified(string table) => $"{QuotedSchema()}.\"{table}\"";

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());
        return (T)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private sealed record RuntimeMigration(string Version, string Name, string Sql)
    {
        public string Checksum { get; } = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Sql))).ToLowerInvariant();
    }

    private sealed record AppliedMigration(string Version, string Name, string Checksum);

    private static readonly IReadOnlyList<RuntimeMigration> Catalog =
    [
        new RuntimeMigration("V001", "runtime_instances", """
            create table {schema}.runtime_workflow_instances (
                tenant_scope_kind text not null,
                tenant_id text not null,
                instance_id text not null,
                revision bigint not null check (revision > 0),
                status integer not null,
                workflow_pin_json jsonb not null,
                waiting_instance_id text null,
                suspension_operation_id text null,
                state_json jsonb not null,
                created_at timestamptz not null default clock_timestamp(),
                updated_at timestamptz not null default clock_timestamp(),
                primary key (tenant_scope_kind, tenant_id, instance_id),
                check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))
            );
            create unique index ux_runtime_workflow_waiting on {schema}.runtime_workflow_instances (tenant_scope_kind, tenant_id, waiting_instance_id)
                where waiting_instance_id is not null;
            create table {schema}.runtime_human_task_instances (
                tenant_scope_kind text not null,
                tenant_id text not null,
                instance_id text not null,
                revision bigint not null check (revision > 0),
                status integer not null,
                human_task_pin_json jsonb not null,
                workflow_instance_id text null,
                workflow_step_id text null,
                suspension_operation_id text null,
                assignee_user_id text null,
                state_json jsonb not null,
                created_at timestamptz not null default clock_timestamp(),
                updated_at timestamptz not null default clock_timestamp(),
                primary key (tenant_scope_kind, tenant_id, instance_id),
                check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> '')),
                check ((workflow_instance_id is null and workflow_step_id is null) or (workflow_instance_id is not null and workflow_step_id is not null))
            );
            create unique index ux_runtime_human_task_active_step on {schema}.runtime_human_task_instances (tenant_scope_kind, tenant_id, workflow_instance_id, workflow_step_id)
                where workflow_instance_id is not null;
            create table {schema}.runtime_operation_receipts (
                tenant_scope_kind text not null,
                tenant_id text not null,
                operation_id text not null,
                workflow_instance_id text not null,
                human_task_instance_id text not null,
                workflow_from_revision bigint not null,
                workflow_to_revision bigint not null,
                integrity_json jsonb not null,
                receipt_json jsonb not null,
                committed_at timestamptz not null default clock_timestamp(),
                primary key (tenant_scope_kind, tenant_id, operation_id),
                check (workflow_to_revision = workflow_from_revision + 1),
                check ((tenant_scope_kind = 'host' and tenant_id = '') or (tenant_scope_kind = 'tenant' and tenant_id <> ''))
            );
            """),
        new RuntimeMigration("V002", "descriptor_snapshot_evidence", """
            create table {schema}.descriptor_snapshots (
                snapshot_id text primary key,
                content_hash text not null,
                snapshot_json jsonb not null,
                created_at timestamptz not null default clock_timestamp()
            );
            create table {schema}.descriptor_snapshot_entries (
                snapshot_id text not null references {schema}.descriptor_snapshots(snapshot_id) on delete restrict,
                descriptor_namespace text not null,
                descriptor_id text not null,
                descriptor_version integer not null,
                contract_hash text not null,
                definition_hash text not null,
                primary key (snapshot_id, descriptor_namespace, descriptor_id, descriptor_version)
            );
            """),
        new RuntimeMigration("V003", "accountability_sink", """
            create table {schema}.runtime_audit_envelopes (
                sink_id text not null,
                audit_id text not null,
                integrity_json jsonb not null,
                envelope_json jsonb not null,
                accepted_at timestamptz not null default clock_timestamp(),
                primary key (sink_id, audit_id)
            );
            """),
        new RuntimeMigration("V004", "reciprocal_fks_and_active_step_lifecycle", """
            alter table {schema}.runtime_human_task_instances
                add column completed_at timestamptz null,
                add column cancelled_at timestamptz null;

            alter table {schema}.runtime_human_task_instances
                add constraint fk_human_task_workflow
                foreign key (tenant_scope_kind, tenant_id, workflow_instance_id)
                references {schema}.runtime_workflow_instances (tenant_scope_kind, tenant_id, instance_id)
                on delete restrict
                deferrable initially deferred;

            alter table {schema}.runtime_workflow_instances
                add constraint fk_workflow_waiting_task
                foreign key (tenant_scope_kind, tenant_id, waiting_instance_id)
                references {schema}.runtime_human_task_instances (tenant_scope_kind, tenant_id, instance_id)
                on delete restrict
                deferrable initially deferred;

            alter table {schema}.runtime_operation_receipts
                add constraint fk_receipt_workflow
                foreign key (tenant_scope_kind, tenant_id, workflow_instance_id)
                references {schema}.runtime_workflow_instances (tenant_scope_kind, tenant_id, instance_id)
                on delete restrict
                deferrable initially deferred;

            alter table {schema}.runtime_operation_receipts
                add constraint fk_receipt_human_task
                foreign key (tenant_scope_kind, tenant_id, human_task_instance_id)
                references {schema}.runtime_human_task_instances (tenant_scope_kind, tenant_id, instance_id)
                on delete restrict
                deferrable initially deferred;

            drop index if exists {schema}.ux_runtime_human_task_active_step;
            create unique index ux_runtime_human_task_active_step
                on {schema}.runtime_human_task_instances (tenant_scope_kind, tenant_id, workflow_instance_id, workflow_step_id)
                where workflow_instance_id is not null
                  and workflow_step_id is not null
                  and completed_at is null
                  and cancelled_at is null;
            """)
    ];
}
