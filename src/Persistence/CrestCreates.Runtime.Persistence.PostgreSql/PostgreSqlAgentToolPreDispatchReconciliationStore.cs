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
        cmd.Parameters.Add(new NpgsqlParameter("logicalKey", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(identity.LogicalInvocationKey) });
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
            values (@tenantId, @logicalKey::jsonb, @attemptId, @revision, @status, @reasonCode, @observedAt)
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
        cmd.Parameters.Add(new NpgsqlParameter("logicalKey", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(observation.Identity.LogicalInvocationKey) });
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
        cmd.Parameters.Add(new NpgsqlParameter("logicalKey", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(identity.LogicalInvocationKey) });
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
            insert into {_options.Schema}.agent_tool_reconciliation_receipts
                (tenant_id, logical_invocation_key, attempt_id, status, reason_code, terminal_at, integrity_value, receipt_json)
            values (@tenantId, @logicalKey::jsonb, @attemptId, @status, @reasonCode, @terminalAt, @integrityValue, @receiptJson::jsonb)
            on conflict (tenant_id, logical_invocation_key, attempt_id) do nothing
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", receipt.Identity.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("logicalKey", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(receipt.Identity.LogicalInvocationKey) });
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", receipt.Identity.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("status", (int)receipt.Status));
        cmd.Parameters.Add(new NpgsqlParameter("reasonCode", receipt.ReasonCode));
        cmd.Parameters.Add(new NpgsqlParameter("terminalAt", receipt.TerminalAt));
        cmd.Parameters.Add(new NpgsqlParameter("integrityValue", receipt.IntegrityValue));
        cmd.Parameters.Add(new NpgsqlParameter("receiptJson", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(receipt) });

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }
}
