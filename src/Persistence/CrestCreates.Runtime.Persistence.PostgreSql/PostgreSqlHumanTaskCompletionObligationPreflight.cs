using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlHumanTaskCompletionObligationPreflight(
    PostgreSqlRuntimePersistenceOptions options,
    NpgsqlDataSource dataSource) : IHumanTaskCompletionObligationPreflight
{
    public async ValueTask ValidateAsync(IReadOnlyList<HumanTaskCompletionObligationPolicyRegistration> policies, IReadOnlySet<string> activeConsumerIds, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_human_tasks");
        await using var command = new NpgsqlCommand($"select state_json::text, required_consumer_ids_json::text from {table} where status in (0,1);", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var task = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(0), PostgreSqlRuntimeJsonSerializerContext.Default.HumanTaskInstance);
            task.RequiredCompletionConsumerIds = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(1), PostgreSqlRuntimeJsonSerializerContext.Default.StringArray);
            foreach (var policy in policies.Where(policy => policy.HumanTaskDescriptorId == task.HumanTaskPin.Ref.Id && policy.HumanTaskDescriptorVersion == task.HumanTaskPin.Ref.Version))
            {
                if (!activeConsumerIds.Contains(policy.RequiredConsumerId))
                    throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, $"HumanTask obligation consumer '{policy.RequiredConsumerId}' is not registered.");
                if (!task.RequiredCompletionConsumerIds.Contains(policy.RequiredConsumerId, StringComparer.Ordinal))
                    throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, $"Active HumanTask '{task.Key.InstanceId}' is missing completion obligation '{policy.RequiredConsumerId}'.");
            }
        }
    }
}
