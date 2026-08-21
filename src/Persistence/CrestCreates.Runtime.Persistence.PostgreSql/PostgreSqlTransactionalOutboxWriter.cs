using System.Text.Json;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
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
        using var lease = session.EnterCommand();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"insert into {_table} (message_id, tenant_id, contract_id, payload_type_id, payload, required_consumer_ids_json, integrity, created_at) values (@id, @tenant, @contract, @type, @payload, @consumers, @integrity, @created);");
        command.Parameters.AddWithValue("id", message.Metadata.MessageId);
        command.Parameters.AddWithValue("tenant", (object?)message.Metadata.TenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("contract", message.Metadata.ContractId);
        command.Parameters.AddWithValue("type", message.Metadata.PayloadTypeId);
        command.Parameters.Add("payload", NpgsqlDbType.Bytea).Value = message.Payload;
        PostgreSqlRuntimeStoreSupport.AddJson(command, "consumers", PostgreSqlRuntimeStoreSupport.Serialize(message.Metadata.RequiredConsumerIds.ToArray(), PostgreSqlRuntimeJsonSerializerContext.Default.StringArray));
        command.Parameters.Add("integrity", NpgsqlDbType.Bytea).Value = message.Integrity;
        command.Parameters.AddWithValue("created", message.Metadata.CreatedAt);
        try { await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); return OutboxAppendResult.Appended; }
        catch (PostgresException ex) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(ex, "runtime_outbox_messages_pkey"))
        {
            await using var read = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"select payload, contract_id, integrity from {_table} where message_id=@id;");
            read.Parameters.AddWithValue("id", message.Metadata.MessageId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw;
            var payload = (byte[])reader[0];
            var contract = reader.GetString(1);
            var integrity = (byte[])reader[2];
            if (!payload.AsSpan().SequenceEqual(message.Payload) || !string.Equals(contract, message.Metadata.ContractId, StringComparison.Ordinal) || !integrity.AsSpan().SequenceEqual(message.Integrity))
                throw new OutboxMessageConflictException($"Outbox message '{message.Metadata.MessageId}' conflicts with an existing message.");
            return OutboxAppendResult.Duplicate;
        }
    }
}
