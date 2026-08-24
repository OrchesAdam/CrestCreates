using System.Text.Json;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlOutboxDispatchStore : IOutboxDispatchStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _table;
    public PostgreSqlOutboxDispatchStore(PostgreSqlRuntimePersistenceOptions options, NpgsqlDataSource dataSource)
    { _options = options; _dataSource = dataSource; _table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_outbox_messages"); }

    public async ValueTask<IReadOnlyList<OutboxDeliveryClaim>> ClaimAsync(OutboxClaimRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var now = await ReadProviderNowAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var rows = new List<OutboxDeliveryClaim>();
        if (request.SupportedContractIds is not null && request.SupportedRequiredConsumerIds is not null)
        {
            await using var active = new NpgsqlCommand($"select contract_id, required_consumer_ids_json::text from {_table} where status in (0,1,2);", connection, transaction);
            await using var reader = await active.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var contract = reader.GetString(0);
                if (!request.SupportedContractIds.Contains(contract))
                    throw new OutboxCompositionException($"Outbox contract '{contract}' is not registered.");
                var consumers = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(1), PostgreSqlRuntimeJsonSerializerContext.Default.StringArray);
                foreach (var consumer in consumers)
                    if (!request.SupportedRequiredConsumerIds.Contains(consumer))
                        throw new OutboxCompositionException($"Outbox required consumer '{consumer}' is not registered.");
            }
        }
        await using (var command = new NpgsqlCommand($"""
            select message_id, tenant_id, contract_id, event_name, event_version, correlation_id, causation_id, occurred_at, payload_utf8, required_consumer_ids_json::text, integrity_json::text, created_at, status, attempt_count, fencing_token
              from {_table}
             where (status in (0,2) and available_at <= @now)
                or (status = 1 and lease_expires_at <= @now)
             order by available_at, occurred_at, message_id collate "C"
             limit @limit for update skip locked;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("now", now); command.Parameters.AddWithValue("limit", request.BatchSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var pending = new List<(string Id, string? Tenant, string Contract, string EventName, int EventVersion, string? Correlation, string? Causation, DateTimeOffset Occurred, byte[] Payload, string Consumers, string Integrity, DateTimeOffset Created, int Attempt, long Fence)>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                pending.Add((reader.GetString(0), reader.IsDBNull(1) || string.IsNullOrEmpty(reader.GetString(1)) ? null : reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7), (byte[])reader[8], reader.GetString(9), reader.GetString(10), reader.GetFieldValue<DateTimeOffset>(11), reader.GetInt32(13), reader.GetInt64(14)));
            await reader.DisposeAsync().ConfigureAwait(false);
            foreach (var row in pending)
            {
                var nextFence = row.Fence + 1; var attempt = row.Attempt + 1; var expires = now + request.LeaseDuration;
                await using var update = new NpgsqlCommand($"update {_table} set status=1, attempt_count=@attempt, fencing_token=@fence, lease_owner_id=@owner, lease_expires_at=@expires, available_at=@available, updated_at=clock_timestamp() where message_id=@id;", connection, transaction);
                update.Parameters.AddWithValue("attempt", attempt); update.Parameters.AddWithValue("fence", nextFence); update.Parameters.AddWithValue("owner", request.OwnerId); update.Parameters.AddWithValue("expires", expires); update.Parameters.AddWithValue("available", expires); update.Parameters.AddWithValue("id", row.Id);
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                var metadata = new OutboxMessageMetadata { MessageId = row.Id, TenantId = row.Tenant, ContractId = row.Contract, PayloadTypeId = row.EventName, RequiredConsumerIds = PostgreSqlRuntimeStoreSupport.Deserialize(row.Consumers, PostgreSqlRuntimeJsonSerializerContext.Default.StringArray), CreatedAt = row.Created, OccurredAt = row.Occurred, EventName = row.EventName, EventVersion = row.EventVersion, CorrelationId = row.Correlation, CausationId = row.Causation };
                rows.Add(new OutboxDeliveryClaim { Message = new OutboxMessage { Metadata = metadata, Payload = row.Payload, Integrity = PostgreSqlRuntimeStoreSupport.Deserialize(row.Integrity, PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash) }, Status = OutboxDeliveryStatus.InFlight, Lease = new OutboxDeliveryLease { OwnerId = request.OwnerId, ExpiresAt = expires, Attempt = attempt, Fence = nextFence } });
            }
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return rows;
    }

    public async ValueTask<DateTimeOffset> GetProviderUtcNowAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadProviderNowAsync(connection, transaction: null, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<OutboxDeliveryMutationResult> AckAsync(string messageId, OutboxDeliveryLease lease, CancellationToken cancellationToken = default)
        => MutateAsync(messageId, lease, "status=3, lease_owner_id=null, lease_expires_at=null, available_at=clock_timestamp(), delivered_at=clock_timestamp(), updated_at=clock_timestamp()", null, cancellationToken);

    public ValueTask<OutboxDeliveryMutationResult> RetryAsync(string messageId, OutboxDeliveryLease lease, OutboxDeliveryFailure failure, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
        => MutateAsync(messageId, lease, "status=2, last_failure_code=@code, last_failure_at=clock_timestamp(), lease_owner_id=null, lease_expires_at=null, available_at=@next, updated_at=clock_timestamp()", (failure.Code, nextAttemptAt), cancellationToken);

    public ValueTask<OutboxDeliveryMutationResult> DeadLetterAsync(string messageId, OutboxDeliveryLease lease, OutboxDeliveryFailure failure, CancellationToken cancellationToken = default)
        => MutateAsync(messageId, lease, "status=4, last_failure_code=@code, last_failure_at=clock_timestamp(), lease_owner_id=null, lease_expires_at=null, available_at=clock_timestamp(), dead_lettered_at=clock_timestamp(), updated_at=clock_timestamp()", (failure.Code, (DateTimeOffset?)null), cancellationToken);

    private async ValueTask<OutboxDeliveryMutationResult> MutateAsync(string id, OutboxDeliveryLease lease, string set, (string Code, DateTimeOffset? Next)? extra, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"update {_table} set {set} where message_id=@id and status=1 and lease_owner_id=@owner and fencing_token=@fence and lease_expires_at > clock_timestamp();", connection, transaction);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("owner", lease.OwnerId); command.Parameters.AddWithValue("fence", lease.Fence);
        if (extra is { } value) { command.Parameters.AddWithValue("code", value.Code); command.Parameters.AddWithValue("next", (object?)value.Next ?? DBNull.Value); }
        var count = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
        return count == 1 ? OutboxDeliveryMutationResult.Applied : OutboxDeliveryMutationResult.StaleLease;
    }

    private static async Task<DateTimeOffset> ReadProviderNowAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select clock_timestamp();", connection, transaction);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value switch
        {
            DateTimeOffset timestamp => timestamp,
            DateTime timestamp => new DateTimeOffset(timestamp.ToUniversalTime()),
            _ => throw new InvalidOperationException("PostgreSQL did not return provider time.")
        };
    }
}
