using System.Text.Json;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
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
        var now = request.Now ?? DateTimeOffset.UtcNow;
        var rows = new List<OutboxDeliveryClaim>();
        await using (var command = new NpgsqlCommand($"""
            select message_id, tenant_id, contract_id, payload_type_id, payload, required_consumer_ids_json::text, integrity, created_at, status, attempt, fence
              from {_table}
             where (status in (0,2) and (next_attempt_at is null or next_attempt_at <= @now))
                or (status = 1 and lease_expires_at <= @now)
             order by created_at, message_id collate "C"
             limit @limit for update skip locked;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("now", now); command.Parameters.AddWithValue("limit", request.BatchSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var pending = new List<(string Id, string? Tenant, string Contract, string Type, byte[] Payload, string Consumers, byte[] Integrity, DateTimeOffset Created, int Attempt, long Fence)>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                pending.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2), reader.GetString(3), (byte[])reader[4], reader.GetString(5), (byte[])reader[6], reader.GetFieldValue<DateTimeOffset>(7), reader.GetInt32(9), reader.GetInt64(10)));
            await reader.DisposeAsync().ConfigureAwait(false);
            foreach (var row in pending)
            {
                var nextFence = row.Fence + 1; var attempt = row.Attempt + 1; var expires = now + request.LeaseDuration;
                await using var update = new NpgsqlCommand($"update {_table} set status=1, attempt=@attempt, fence=@fence, lease_owner=@owner, lease_expires_at=@expires, next_attempt_at=null where message_id=@id;", connection, transaction);
                update.Parameters.AddWithValue("attempt", attempt); update.Parameters.AddWithValue("fence", nextFence); update.Parameters.AddWithValue("owner", request.OwnerId); update.Parameters.AddWithValue("expires", expires); update.Parameters.AddWithValue("id", row.Id);
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                var metadata = new OutboxMessageMetadata { MessageId = row.Id, TenantId = row.Tenant, ContractId = row.Contract, PayloadTypeId = row.Type, RequiredConsumerIds = PostgreSqlRuntimeStoreSupport.Deserialize(row.Consumers, PostgreSqlRuntimeJsonSerializerContext.Default.StringArray), CreatedAt = row.Created };
                rows.Add(new OutboxDeliveryClaim { Message = new OutboxMessage { Metadata = metadata, Payload = row.Payload, Integrity = row.Integrity }, Status = OutboxDeliveryStatus.InFlight, Lease = new OutboxDeliveryLease { OwnerId = request.OwnerId, ExpiresAt = expires, Attempt = attempt, Fence = nextFence } });
            }
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return rows;
    }

    public ValueTask<OutboxDeliveryMutationResult> AckAsync(string messageId, OutboxDeliveryLease lease, CancellationToken cancellationToken = default)
        => MutateAsync(messageId, lease, "status=3, lease_owner=null, lease_expires_at=null, next_attempt_at=null", null, cancellationToken);

    public ValueTask<OutboxDeliveryMutationResult> RetryAsync(string messageId, OutboxDeliveryLease lease, OutboxDeliveryFailure failure, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
        => MutateAsync(messageId, lease, "status=2, last_failure_code=@code, lease_owner=null, lease_expires_at=null, next_attempt_at=@next", (failure.Code, nextAttemptAt), cancellationToken);

    public ValueTask<OutboxDeliveryMutationResult> DeadLetterAsync(string messageId, OutboxDeliveryLease lease, OutboxDeliveryFailure failure, CancellationToken cancellationToken = default)
        => MutateAsync(messageId, lease, "status=4, last_failure_code=@code, lease_owner=null, lease_expires_at=null, next_attempt_at=null", (failure.Code, (DateTimeOffset?)null), cancellationToken);

    private async ValueTask<OutboxDeliveryMutationResult> MutateAsync(string id, OutboxDeliveryLease lease, string set, (string Code, DateTimeOffset? Next)? extra, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"update {_table} set {set} where message_id=@id and status=1 and lease_owner=@owner and fence=@fence;", connection, transaction);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("owner", lease.OwnerId); command.Parameters.AddWithValue("fence", lease.Fence);
        if (extra is { } value) { command.Parameters.AddWithValue("code", value.Code); command.Parameters.AddWithValue("next", (object?)value.Next ?? DBNull.Value); }
        var count = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false); await transaction.CommitAsync(ct).ConfigureAwait(false);
        return count == 1 ? OutboxDeliveryMutationResult.Applied : OutboxDeliveryMutationResult.StaleLease;
    }
}
