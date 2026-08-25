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
        var table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_human_task_instances");
        await using (var legacy = new NpgsqlCommand($"select instance_id from {table} where status = 4 order by tenant_id, instance_id limit 1;", connection))
        {
            var legacyId = await legacy.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (legacyId is not null)
                throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, $"Legacy HumanTask '{legacyId}' is in CompletionDispatchFailed and must be explicitly reconciled before transactional outbox cutover.");
        }

        foreach (var policy in policies)
        {
            if (!activeConsumerIds.Contains(policy.RequiredConsumerId))
                throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, $"HumanTask obligation consumer '{policy.RequiredConsumerId}' is not registered.");

            await using var command = new NpgsqlCommand($@"
                with gaps as (
                    select tenant_id, instance_id
                    from {table}
                    where status in (0, 1)
                      and human_task_pin_json #>> '{{ref,id}}' = @descriptor_id
                      and (human_task_pin_json #>> '{{ref,version}}')::integer = @descriptor_version
                      and not (required_consumer_ids_json @> jsonb_build_array(@consumer_id))
                ), sample as (
                    select instance_id from gaps order by tenant_id, instance_id limit 10
                )
                select (select count(*) from gaps),
                       coalesce((select array_agg(instance_id order by instance_id) from sample), ARRAY[]::text[]);
                ", connection);
            command.Parameters.AddWithValue("descriptor_id", policy.HumanTaskDescriptorId);
            command.Parameters.AddWithValue("descriptor_version", policy.HumanTaskDescriptorVersion);
            command.Parameters.AddWithValue("consumer_id", policy.RequiredConsumerId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var gapCount = reader.GetInt64(0);
            if (gapCount > 0)
            {
                var sampleIds = reader.IsDBNull(1) ? [] : reader.GetFieldValue<string[]>(1);
                throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                    $"Active HumanTask completion obligation gap for '{policy.RequiredConsumerId}': {gapCount} row(s), sample [{string.Join(", ", sampleIds)}].");
            }
        }
    }
}
