using System.Text.Json;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlTransactionalOutboxWriter : ITransactionalOutboxWriter
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly string _table;
    public PostgreSqlTransactionalOutboxWriter(PostgreSqlRuntimePersistenceOptions options, PostgreSqlRuntimeTransactionCoordinator coordinator)
    { _options = options; _coordinator = coordinator; _table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_outbox_messages"); }

    public async ValueTask<OutboxAppendResult> AppendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (!OutboxMessageIntegrity.Matches(message))
            throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, "Outbox message integrity does not match its immutable payload.");
        var session = _coordinator.RequireSession();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"insert into {_table} (message_id, contract_id, event_name, event_version, tenant_scope_kind, tenant_id, correlation_id, causation_id, occurred_at, required_consumer_ids_json, payload_utf8, integrity_json, created_at, available_at, updated_at) values (@id, @contract, @eventName, @eventVersion, @scope, @tenant, @correlation, @causation, @occurred, @consumers, @payload, @integrity, clock_timestamp(), clock_timestamp(), clock_timestamp()) on conflict (message_id) do nothing;");
        command.Parameters.AddWithValue("id", message.Metadata.MessageId);
        command.Parameters.AddWithValue("contract", message.Metadata.ContractId);
        command.Parameters.AddWithValue("eventName", message.Metadata.EventName);
        command.Parameters.AddWithValue("eventVersion", message.Metadata.EventVersion);
        command.Parameters.AddWithValue("scope", PostgreSqlRuntimeStoreSupport.ScopeKind(message.Metadata.TenantId));
        command.Parameters.AddWithValue("tenant", PostgreSqlRuntimeStoreSupport.TenantValue(message.Metadata.TenantId));
        command.Parameters.AddWithValue("correlation", (object?)message.Metadata.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("causation", (object?)message.Metadata.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue("occurred", message.Metadata.OccurredAt);
        command.Parameters.Add("payload", NpgsqlDbType.Bytea).Value = message.Payload;
        PostgreSqlRuntimeStoreSupport.AddJson(command, "consumers", PostgreSqlRuntimeStoreSupport.Serialize(message.Metadata.RequiredConsumerIds.ToArray(), PostgreSqlRuntimeJsonSerializerContext.Default.StringArray));
        PostgreSqlRuntimeStoreSupport.AddJson(command, "integrity", PostgreSqlRuntimeStoreSupport.Serialize(message.Integrity, PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash));
        int inserted;
        using (session.EnterCommand())
            inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (inserted == 1) return OutboxAppendResult.Appended;

        await using var read = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"select payload_utf8, contract_id, integrity_json::text from {_table} where message_id=@id;");
        read.Parameters.AddWithValue("id", message.Metadata.MessageId);
        byte[] payload;
        string contract;
        CanonicalHash integrity;
        using (session.EnterCommand())
        {
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, "Outbox duplicate probe found no durable row.");
            payload = (byte[])reader[0];
            contract = reader.GetString(1);
            integrity = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(2), PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash);
        }
        if (!payload.AsSpan().SequenceEqual(message.Payload) || !string.Equals(contract, message.Metadata.ContractId, StringComparison.Ordinal) || integrity != message.Integrity)
            throw new OutboxMessageConflictException($"Outbox message '{message.Metadata.MessageId}' conflicts with an existing message.");
        return OutboxAppendResult.Duplicate;
    }
}
