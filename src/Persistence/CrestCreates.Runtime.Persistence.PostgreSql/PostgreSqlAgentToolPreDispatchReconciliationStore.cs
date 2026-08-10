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
            ReasonCode = DecodeReasonCode(reader.GetString(2)),
            ObservedAt = reader.GetFieldValue<DateTimeOffset>(3)
        };
    }

    private async ValueTask<bool> TryUpsertObservationCoreAsync(
        AgentToolPreDispatchReconciliationObservation observation,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        var connection = _coordinator.RequireSession().Connection;
        await AcquireIdentityLockAsync(connection, observation.Identity, cancellationToken).ConfigureAwait(false);

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
        cmd.Parameters.Add(new NpgsqlParameter("reasonCode", EncodeReasonCode(observation.ReasonCode)));
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
            ReasonCode = DecodeReasonCode(reader.GetString(1)),
            TerminalAt = reader.GetFieldValue<DateTimeOffset>(2),
            IntegrityValue = reader.GetString(3)
        };
    }

    private async ValueTask<bool> TryInsertReceiptCoreAsync(
        AgentToolPreDispatchReconciliationReceipt receipt,
        CancellationToken cancellationToken)
    {
        var connection = _coordinator.RequireSession().Connection;
        await AcquireIdentityLockAsync(connection, receipt.Identity, cancellationToken).ConfigureAwait(false);

        var logicalKeyJson = PostgreSqlRuntimeStoreSupport.Serialize(
            receipt.Identity.LogicalInvocationKey,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey);

        bool inserted;
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = $"""
                insert into {_options.Schema}.agent_tool_reconciliation_receipts
                    (tenant_id, logical_invocation_key, attempt_id, status, reason_code, terminal_at, integrity_value, receipt_json)
                values (@tenantId, @logicalKey::jsonb, @attemptId, @status, @reasonCode, @terminalAt, @integrityValue, @receiptJson::jsonb)
                on conflict (tenant_id, logical_invocation_key, attempt_id) do nothing
                returning 1
                """;
            insert.Parameters.Add(new NpgsqlParameter("tenantId", receipt.Identity.LogicalInvocationKey.TenantId ?? string.Empty));
            insert.Parameters.Add(new NpgsqlParameter("logicalKey", NpgsqlDbType.Jsonb) { Value = logicalKeyJson });
            insert.Parameters.Add(new NpgsqlParameter("attemptId", receipt.Identity.AttemptId));
            insert.Parameters.Add(new NpgsqlParameter("status", (int)receipt.Status));
            insert.Parameters.Add(new NpgsqlParameter("reasonCode", EncodeReasonCode(receipt.ReasonCode)));
            insert.Parameters.Add(new NpgsqlParameter("terminalAt", receipt.TerminalAt));
            insert.Parameters.Add(new NpgsqlParameter("integrityValue", receipt.IntegrityValue));
            insert.Parameters.Add(new NpgsqlParameter("receiptJson", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(receipt, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolPreDispatchReconciliationReceipt) });

            inserted = await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
        }

        await using (var clearObservation = connection.CreateCommand())
        {
            clearObservation.CommandText = $"""
                delete from {_options.Schema}.agent_tool_reconciliation_observations
                where tenant_id = @tenantId
                  and logical_invocation_key = @logicalKey::jsonb
                  and attempt_id = @attemptId
                """;
            clearObservation.Parameters.Add(new NpgsqlParameter("tenantId", receipt.Identity.LogicalInvocationKey.TenantId ?? string.Empty));
            clearObservation.Parameters.Add(new NpgsqlParameter("logicalKey", NpgsqlDbType.Jsonb) { Value = logicalKeyJson });
            clearObservation.Parameters.Add(new NpgsqlParameter("attemptId", receipt.Identity.AttemptId));
            await clearObservation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return inserted;
    }

    private static async ValueTask AcquireIdentityLockAsync(
        NpgsqlConnection connection,
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken)
    {
        // A terminal receipt and its mutable observation must transition as one identity-scoped aggregate.
        await using var command = connection.CreateCommand();
        command.CommandText = "select pg_advisory_xact_lock(hashtextextended(@identity, 0));";
        command.Parameters.Add(new NpgsqlParameter(
            "identity",
            $"{PostgreSqlRuntimeStoreSupport.Serialize(identity.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey)}\n{identity.AttemptId}"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // The reconciliation tables keep reason_code as NOT NULL text (schema is frozen). The runtime
    // semantic null (a conflict/observation with no reason family) is encoded as the empty string so
    // it can round-trip without a schema change, and decoded back to null on read.
    private static object EncodeReasonCode(string? reasonCode)
        => reasonCode ?? string.Empty;

    private static string? DecodeReasonCode(string value)
        => value.Length == 0 ? null : value;
}
