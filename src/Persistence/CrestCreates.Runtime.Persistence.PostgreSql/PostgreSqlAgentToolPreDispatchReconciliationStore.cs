using System.Text.Json;
using CrestCreates.Agent.Tools;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlAgentToolPreDispatchReconciliationStore : IAgentToolPreDispatchReconciliationStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;

    public PostgreSqlAgentToolPreDispatchReconciliationStore(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public ValueTask<AgentToolPreDispatchReconciliationObservation?> ReadObservationAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => ReadObservationCoreAsync(identity, ct), cancellationToken);

    public ValueTask<bool> TryUpsertObservationAsync(
        AgentToolPreDispatchReconciliationObservation observation,
        long expectedRevision,
        CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => TryUpsertObservationCoreAsync(observation, expectedRevision, ct), cancellationToken);

    public ValueTask<AgentToolPreDispatchReconciliationReceipt?> ReadReceiptAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => ReadReceiptCoreAsync(identity, ct), cancellationToken);

    public ValueTask<bool> TryInsertReceiptAsync(
        AgentToolPreDispatchReconciliationReceipt receipt,
        CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => TryInsertReceiptCoreAsync(receipt, ct), cancellationToken);

    private async ValueTask<AgentToolPreDispatchReconciliationObservation?> ReadObservationCoreAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken)
    {
        var connection = _coordinator.RequireSession().Connection;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            select revision, status, reason_code, observed_at
            from {_options.Schema}.agent_tool_reconciliation_observations
            where tenant_id = @tenantId
              and logical_invocation_key = @logicalKey::jsonb
              and attempt_id = @attemptId
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", identity.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("logicalKey", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(identity.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey) });
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", identity.AttemptId));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new AgentToolPreDispatchReconciliationObservation
        {
            Identity = identity,
            Revision = reader.GetInt64(0),
            Status = (AgentToolPreDispatchReconciliationStatus)reader.GetInt32(1),
            ReasonCode = reader.IsDBNull(2) ? null : reader.GetString(2),
            ObservedAt = reader.GetFieldValue<DateTimeOffset>(3)
        };
    }

    private async ValueTask<bool> TryUpsertObservationCoreAsync(
        AgentToolPreDispatchReconciliationObservation observation,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        var connection = _coordinator.RequireSession().Connection;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            insert into {_options.Schema}.agent_tool_reconciliation_observations
                (tenant_id, logical_invocation_key, attempt_id, revision, status, reason_code, observed_at)
            select @tenantId, @logicalKey::jsonb, @attemptId, @revision, @status, @reasonCode, @observedAt
            where not exists (
                select 1
                from {_options.Schema}.agent_tool_reconciliation_receipts r
                where r.tenant_id = @tenantId
                  and r.logical_invocation_key = @logicalKey::jsonb
                  and r.attempt_id = @attemptId)
            on conflict (tenant_id, logical_invocation_key, attempt_id)
            do update set
                revision = excluded.revision,
                status = excluded.status,
                reason_code = excluded.reason_code,
                observed_at = excluded.observed_at
            where agent_tool_reconciliation_observations.revision = @expectedRevision
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", observation.Identity.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("logicalKey", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(observation.Identity.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey) });
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", observation.Identity.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("revision", observation.Revision));
        cmd.Parameters.Add(new NpgsqlParameter("status", (int)observation.Status));
        cmd.Parameters.Add(new NpgsqlParameter("reasonCode", observation.ReasonCode ?? (object)DBNull.Value));
        cmd.Parameters.Add(new NpgsqlParameter("observedAt", observation.ObservedAt));
        cmd.Parameters.Add(new NpgsqlParameter("expectedRevision", expectedRevision));

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    private async ValueTask<AgentToolPreDispatchReconciliationReceipt?> ReadReceiptCoreAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken)
    {
        var connection = _coordinator.RequireSession().Connection;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            select status, reason_code, terminal_at, integrity_value
            from {_options.Schema}.agent_tool_reconciliation_receipts
            where tenant_id = @tenantId
              and logical_invocation_key = @logicalKey::jsonb
              and attempt_id = @attemptId
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", identity.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("logicalKey", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(identity.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey) });
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", identity.AttemptId));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new AgentToolPreDispatchReconciliationReceipt
        {
            Identity = identity,
            Status = (AgentToolPreDispatchReconciliationStatus)reader.GetInt32(0),
            ReasonCode = reader.GetString(1),
            TerminalAt = reader.GetFieldValue<DateTimeOffset>(2),
            IntegrityValue = reader.GetString(3)
        };
    }

    private async ValueTask<bool> TryInsertReceiptCoreAsync(
        AgentToolPreDispatchReconciliationReceipt receipt,
        CancellationToken cancellationToken)
    {
        var connection = _coordinator.RequireSession().Connection;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            with inserted as (
                insert into {_options.Schema}.agent_tool_reconciliation_receipts
                    (tenant_id, logical_invocation_key, attempt_id, status, reason_code, terminal_at, integrity_value, receipt_json)
                values (@tenantId, @logicalKey::jsonb, @attemptId, @status, @reasonCode, @terminalAt, @integrityValue, @receiptJson::jsonb)
                on conflict (tenant_id, logical_invocation_key, attempt_id) do nothing
                returning 1
            ), cleared_observation as (
                delete from {_options.Schema}.agent_tool_reconciliation_observations o
                where o.tenant_id = @tenantId
                  and o.logical_invocation_key = @logicalKey::jsonb
                  and o.attempt_id = @attemptId
                  and exists (
                      select 1
                      from {_options.Schema}.agent_tool_reconciliation_receipts r
                      where r.tenant_id = o.tenant_id
                        and r.logical_invocation_key = o.logical_invocation_key
                        and r.attempt_id = o.attempt_id)
                returning 1
            )
            select exists(select 1 from inserted)
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", receipt.Identity.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("logicalKey", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(receipt.Identity.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey) });
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", receipt.Identity.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("status", (int)receipt.Status));
        cmd.Parameters.Add(new NpgsqlParameter("reasonCode", receipt.ReasonCode));
        cmd.Parameters.Add(new NpgsqlParameter("terminalAt", receipt.TerminalAt));
        cmd.Parameters.Add(new NpgsqlParameter("integrityValue", receipt.IntegrityValue));
        cmd.Parameters.Add(new NpgsqlParameter("receiptJson", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(receipt, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolPreDispatchReconciliationReceipt) });

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is true;
    }
}
