using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlOutboxCompositionProbe(PostgreSqlRuntimePersistenceOptions options, NpgsqlDataSource dataSource) : IOutboxCompositionProbe
{
    public async ValueTask ValidateAsync(ActiveOutboxRequirements requirements, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_outbox_messages");
        await using var command = new NpgsqlCommand($"select contract_id, required_consumer_ids_json::text from {table} where status in (0, 1, 2);", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var contractId = reader.GetString(0);
            if (!requirements.ContractIds.Contains(contractId))
                throw new OutboxCompositionException($"Outbox contract '{contractId}' is not registered.");

            var consumerIds = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(1), PostgreSqlRuntimeJsonSerializerContext.Default.StringArray);
            foreach (var consumerId in consumerIds)
            {
                if (string.IsNullOrWhiteSpace(consumerId))
                    throw new OutboxCompositionException("Outbox required consumer IDs must be non-empty strings.");
                if (!requirements.ConsumerIds.Contains(consumerId))
                    throw new OutboxCompositionException($"Outbox required consumer '{consumerId}' is not registered.");
            }
        }
    }
}
