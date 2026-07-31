using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

public sealed class PostgreSqlRuntimeMigrationRunner
{
    private const string HistoryTable = "crest_runtime_schema_migrations";
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    public PostgreSqlRuntimeMigrationRunner(PostgreSqlRuntimePersistenceOptions options) => _options = options;

    public async Task ApplyAsync(PostgreSqlRuntimeMigrationOptions migrationOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migrationOptions);
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!migrationOptions.ApplyMigrations)
        {
            await ValidateOnlyAsync(connection, cancellationToken).ConfigureAwait(false);
            return;
        }
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var create = new NpgsqlCommand($"create schema if not exists \"{_options.Schema.Replace("\"", "\"\"")}\"; create table if not exists \"{_options.Schema.Replace("\"", "\"\"")}\".{HistoryTable} (version text primary key, checksum text not null, applied_at timestamptz not null);", connection, transaction))
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using (var migration = new NpgsqlCommand(BuildV001(), connection, transaction))
            await migration.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateOnlyAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("select exists (select 1 from information_schema.schemata where schema_name=@schema) and exists (select 1 from information_schema.tables where table_schema=@schema and table_name=@history)", connection);
        command.Parameters.AddWithValue("schema", _options.Schema); command.Parameters.AddWithValue("history", HistoryTable);
        if ((bool?)await command.ExecuteScalarAsync(ct).ConfigureAwait(false) != true)
            throw new InvalidOperationException("PostgreSQL runtime schema/history is missing; validation-only mode executes no DDL.");
    }

    private string BuildV001()
    {
        var schema = $"\"{_options.Schema.Replace("\"", "\"\"")}\"";
        return $"create table if not exists {schema}.runtime_workflow_instances (tenant_scope_kind text not null, tenant_id text not null, instance_id text not null, revision bigint not null, status integer not null, workflow_namespace text not null, workflow_id text not null, workflow_version integer not null, contract_hash text not null, definition_hash text not null, waiting_scope_kind text null, waiting_tenant_id text null, waiting_instance_id text null, updated_at timestamptz not null default clock_timestamp(), primary key (tenant_scope_kind, tenant_id, instance_id)); create unique index if not exists ux_runtime_workflow_waiting on {schema}.runtime_workflow_instances (waiting_scope_kind, waiting_tenant_id, waiting_instance_id) where waiting_instance_id is not null; create table if not exists {schema}.runtime_human_task_instances (tenant_scope_kind text not null, tenant_id text not null, instance_id text not null, revision bigint not null, status integer not null, human_task_namespace text not null, human_task_id text not null, human_task_version integer not null, contract_hash text not null, definition_hash text not null, workflow_scope_kind text null, workflow_tenant_id text null, workflow_instance_id text null, outcome text null, assignee_user_id text null, primary key (tenant_scope_kind, tenant_id, instance_id)); create table if not exists {schema}.runtime_audit_envelopes (audit_id text primary key, integrity_value text not null, accepted_at timestamptz not null); insert into {schema}.{HistoryTable} (version, checksum) values ('V001','phase9b-v001') on conflict (version) do nothing;";
    }
}
