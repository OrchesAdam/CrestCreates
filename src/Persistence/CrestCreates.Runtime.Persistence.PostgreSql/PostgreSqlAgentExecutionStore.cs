using CrestCreates.Agent.Orchestration;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using Npgsql;
using static CrestCreates.Runtime.Persistence.PostgreSql.PostgreSqlRuntimeStoreSupport;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlAgentExecutionStore(
    NpgsqlDataSource dataSource, PostgreSqlRuntimePersistenceOptions options) : IAgentExecutionStore
{
    private const string KeyPredicate = "tenant_scope_kind = @scope and tenant_id = @tenant and root_run_id = @root";
    private readonly string _table = Table(options, "agent_executions");

    public async ValueTask<AgentExecutionSnapshot?> FindAsync(AgentExecutionKey key, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"select revision, state_json from {_table} where {KeyPredicate};", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var stored = reader.GetString(1);
        // jsonb cannot hold U+0000 inside a JSON string. Store the opaque snapshot as a
        // JSON string so escaped control characters remain lossless at the public boundary.
        var payload = stored.StartsWith('"')
            ? Deserialize(stored, PostgreSqlRuntimeJsonSerializerContext.Default.String)
            : stored;
        var execution = Deserialize(payload, PostgreSqlRuntimeJsonSerializerContext.Default.AgentExecutionSnapshot);
        if (execution.Key != key || execution.Revision != reader.GetInt64(0))
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                "The persisted Agent execution snapshot does not match its scoped key or revision.");
        }

        return execution;
    }

    public async ValueTask<bool> TryCreateAsync(AgentExecutionSnapshot execution, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (execution.Revision != 0)
        {
            return false;
        }

        await using var command = CreateCommand($"""
            insert into {_table} (tenant_scope_kind, tenant_id, root_run_id, revision, state_json)
            values (@scope, @tenant, @root, @revision, @state)
            on conflict (tenant_scope_kind, tenant_id, root_run_id) do nothing;
            """, execution.Key);
        AddSnapshot(command, execution);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async ValueTask<bool> TryReplaceAsync(
        AgentExecutionSnapshot execution, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRevision < 0 || expectedRevision == long.MaxValue || execution.Revision != expectedRevision + 1)
        {
            return false;
        }

        await using var command = CreateCommand($"""
            update {_table} set revision = @revision, state_json = @state
            where {KeyPredicate} and revision = @expected;
            """, execution.Key);
        command.Parameters.AddWithValue("expected", expectedRevision);
        AddSnapshot(command, execution);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private NpgsqlCommand CreateCommand(string sql, AgentExecutionKey key)
    {
        var scope = new RuntimeTenantScope(key.TenantId);
        var command = dataSource.CreateCommand(sql);
        command.CommandTimeout = options.CommandTimeoutSeconds;
        AddScope(command, scope);
        command.Parameters.AddWithValue("root", key.RootRunId);
        return command;
    }

    private static void AddSnapshot(NpgsqlCommand command, AgentExecutionSnapshot execution)
    {
        command.Parameters.AddWithValue("revision", execution.Revision);
        var payload = Serialize(execution, PostgreSqlRuntimeJsonSerializerContext.Default.AgentExecutionSnapshot);
        AddJson(command, "state", Serialize(payload, PostgreSqlRuntimeJsonSerializerContext.Default.String));
    }
}
