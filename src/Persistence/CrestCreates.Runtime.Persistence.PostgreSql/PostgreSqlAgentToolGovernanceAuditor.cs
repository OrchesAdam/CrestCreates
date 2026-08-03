using System.Text.Json;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlAgentToolGovernanceAuditor : IAgentToolGovernanceAuditor
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;

    public PostgreSqlAgentToolGovernanceAuditor(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public ValueTask RecordDecisionAsync(AgentToolGovernanceDecisionRecord record, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => RecordDecisionCoreAsync(record, ct), cancellationToken);

    public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(AgentToolGovernancePreDispatchRecord record, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => RecordPreDispatchCoreAsync(record, ct), cancellationToken);

    public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetPreDispatchStateCoreAsync(identity, ct), cancellationToken);

    public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(AgentToolGovernanceFinalizationRecord record, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => FinalizeCoreAsync(record, ct), cancellationToken);

    public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(string auditId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetFinalizationStateCoreAsync(auditId, ct), cancellationToken);

    private NpgsqlConnection Conn() => _coordinator.RequireSession().Connection;

    private async ValueTask RecordDecisionCoreAsync(AgentToolGovernanceDecisionRecord record, CancellationToken cancellationToken)
    {
        var auditId = Guid.NewGuid().ToString("N");
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            insert into {_options.Schema}.agent_tool_governance_decisions
                (tenant_id, audit_id, logical_invocation_key, attempt_id, decision_state, decision_json)
            values (@tenantId, @auditId, @lik, @attemptId, @decisionState, @decisionJson)
            on conflict (tenant_id, audit_id) do nothing
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", record.Context.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("auditId", auditId));
        cmd.Parameters.Add(new NpgsqlParameter("lik", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(record.Context.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey) });
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", record.Context.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("decisionState", (int)record.Decision));
        cmd.Parameters.Add(new NpgsqlParameter("decisionJson", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(record, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolGovernanceDecisionRecord) });
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchCoreAsync(AgentToolGovernancePreDispatchRecord record, CancellationToken cancellationToken)
    {
        var connection = _coordinator.RequireSession().Connection;
        var acceptedAt = DateTimeOffset.UtcNow;
        var receipt = new AgentToolGovernancePreDispatchReceipt
        {
            AuditId = Guid.NewGuid().ToString("N"),
            Identity = new AgentToolPreDispatchIdentity(record.Context.LogicalInvocationKey, record.Context.AttemptId),
            AcceptedAt = acceptedAt
        };

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            insert into {_options.Schema}.agent_tool_pre_dispatch_checkpoints
                (tenant_id, audit_id, logical_invocation_key, attempt_id, invocation_fingerprint, arguments_hash,
                 arguments_evaluated, call_origin, agent_roles_hash, tool_contract_json, capability_contract_json,
                 input_schema_contract_json, output_schema_contract_json, governance_json, lease_json, approval_json,
                 budget_reservation_json, accepted_at)
            values (@tenantId, @auditId, @logicalKey::jsonb, @attemptId, @fp, @argsHash,
                    @argsEval, @callOrigin, @rolesHash, @toolJson::jsonb, @capJson::jsonb,
                    @inputJson::jsonb, @outputJson::jsonb, @govJson::jsonb, @leaseJson::jsonb, @approvalJson::jsonb,
                    @budgetJson::jsonb, @acceptedAt)
            on conflict (tenant_id, logical_invocation_key, attempt_id) do nothing
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("auditId", NpgsqlDbType.Text) { Value = receipt.AuditId });
        AddCheckpointParameters(cmd, record, acceptedAt);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            var existing = await GetPreDispatchStateCoreAsync(receipt.Identity, cancellationToken).ConfigureAwait(false);
            if (existing.Status == AgentToolGovernancePreDispatchReadStatus.Accepted && existing.Checkpoint is not null)
            {
                if (AgentToolGovernancePreDispatchComparer.Equivalent(existing.Checkpoint, record))
                    return new AgentToolGovernancePreDispatchWriteResult { Status = AgentToolGovernancePreDispatchWriteStatus.Duplicate, Receipt = existing.Receipt };
                return new AgentToolGovernancePreDispatchWriteResult { Status = AgentToolGovernancePreDispatchWriteStatus.Conflict };
            }
            return new AgentToolGovernancePreDispatchWriteResult { Status = AgentToolGovernancePreDispatchWriteStatus.Conflict };
        }

        return new AgentToolGovernancePreDispatchWriteResult { Status = AgentToolGovernancePreDispatchWriteStatus.Accepted, Receipt = receipt };
    }

    private async ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateCoreAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken)
    {
        var connection = _coordinator.RequireSession().Connection;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            select audit_id, logical_invocation_key, attempt_id, invocation_fingerprint, arguments_hash, arguments_evaluated, call_origin, agent_roles_hash,
                   tool_contract_json, capability_contract_json, input_schema_contract_json, output_schema_contract_json,
                   governance_json, lease_json, approval_json, budget_reservation_json, accepted_at
            from {_options.Schema}.agent_tool_pre_dispatch_checkpoints
            where tenant_id = @tenantId
              and logical_invocation_key = @logicalKey::jsonb
              and attempt_id = @attemptId
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", identity.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("logicalKey", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(identity.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey) });
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", identity.AttemptId));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return new AgentToolGovernancePreDispatchReadResult { Status = AgentToolGovernancePreDispatchReadStatus.Missing };

        var auditId = reader.GetString(reader.GetOrdinal("audit_id"));
        var acceptedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("accepted_at"));
        var checkpoint = ReadCheckpoint(reader);

        return new AgentToolGovernancePreDispatchReadResult
        {
            Status = AgentToolGovernancePreDispatchReadStatus.Accepted,
            Receipt = new AgentToolGovernancePreDispatchReceipt
            {
                AuditId = auditId,
                Identity = identity,
                AcceptedAt = acceptedAt
            },
            Checkpoint = checkpoint
        };
    }

    private async ValueTask<AgentToolGovernanceFinalizationResult> FinalizeCoreAsync(AgentToolGovernanceFinalizationRecord record, CancellationToken cancellationToken)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            insert into {_options.Schema}.agent_tool_governance_finalizations
                (tenant_id, audit_id, logical_invocation_key, attempt_id, attempt_state, finalization_json)
            values (@tenantId, @auditId, @lik, @attemptId, @attemptState, @finalizationJson)
            on conflict (tenant_id, audit_id) do update set
                attempt_state = excluded.attempt_state,
                finalization_json = excluded.finalization_json
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", record.Context.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("auditId", record.AuditId));
        cmd.Parameters.Add(new NpgsqlParameter("lik", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(record.Context.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey) });
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", record.Context.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("attemptState", (int)record.AttemptState));
        cmd.Parameters.Add(new NpgsqlParameter("finalizationJson", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(record, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolGovernanceFinalizationRecord) });
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return new AgentToolGovernanceFinalizationResult
        {
            Status = result is not null ? AgentToolGovernanceFinalizationStatus.Finalized : AgentToolGovernanceFinalizationStatus.NotFinalized
        };
    }

    private async ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateCoreAsync(string auditId, CancellationToken cancellationToken)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select 1 from {_options.Schema}.agent_tool_governance_finalizations
            where audit_id = @auditId
            """;
        cmd.Parameters.Add(new NpgsqlParameter("auditId", auditId));
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return new AgentToolGovernanceFinalizationResult
        {
            Status = result is not null ? AgentToolGovernanceFinalizationStatus.Finalized : AgentToolGovernanceFinalizationStatus.NotFinalized
        };
    }

    private static void AddCheckpointParameters(NpgsqlCommand cmd, AgentToolGovernancePreDispatchRecord record, DateTimeOffset acceptedAt)
    {
        var ctx = record.Context;
        var tenantId = ctx.LogicalInvocationKey.TenantId ?? string.Empty;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        cmd.Parameters.Add(new NpgsqlParameter("logicalKey", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(ctx.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey) });
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", ctx.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("fp", ctx.InvocationFingerprint ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("argsHash", (object?)ctx.ArgumentsHash ?? DBNull.Value));
        cmd.Parameters.Add(new NpgsqlParameter("argsEval", ctx.ArgumentsEvaluated));
        cmd.Parameters.Add(new NpgsqlParameter("callOrigin", (int)ctx.CallOrigin));
        cmd.Parameters.Add(new NpgsqlParameter("rolesHash", (object?)ctx.AgentRolesHash ?? DBNull.Value));
        cmd.Parameters.Add(new NpgsqlParameter("toolJson", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(ctx.ToolContract, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolContractIdentity) });
        cmd.Parameters.Add(new NpgsqlParameter("capJson", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(ctx.CapabilityContract, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolContractIdentity) });
        cmd.Parameters.Add(new NpgsqlParameter("inputJson", NpgsqlDbType.Jsonb) { Value = ctx.InputSchemaContract is not null ? PostgreSqlRuntimeStoreSupport.Serialize(ctx.InputSchemaContract, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolSchemaContractIdentity) : DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("outputJson", NpgsqlDbType.Jsonb) { Value = ctx.OutputSchemaContract is not null ? PostgreSqlRuntimeStoreSupport.Serialize(ctx.OutputSchemaContract, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolSchemaContractIdentity) : DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("govJson", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(ctx.Governance, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolEffectiveGovernance) });
        cmd.Parameters.Add(new NpgsqlParameter("leaseJson", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(record.Lease, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationLease) });
        cmd.Parameters.Add(new NpgsqlParameter("approvalJson", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(record.Approval, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolApprovalResult) });
        cmd.Parameters.Add(new NpgsqlParameter("budgetJson", NpgsqlDbType.Jsonb) { Value = PostgreSqlRuntimeStoreSupport.Serialize(record.BudgetReservation, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolBudgetReservation) });
        cmd.Parameters.Add(new NpgsqlParameter("acceptedAt", acceptedAt));
    }

    private static AgentToolGovernancePreDispatchRecord ReadCheckpoint(NpgsqlDataReader reader)
    {
        var ctx = new AgentToolGovernanceAuditContext
        {
            LogicalInvocationKey = DeserializeJson(reader, "logical_invocation_key", PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey),
            AttemptId = reader.GetString(reader.GetOrdinal("attempt_id")),
            InvocationFingerprint = reader.GetString(reader.GetOrdinal("invocation_fingerprint")),
            ArgumentsHash = reader.IsDBNull(reader.GetOrdinal("arguments_hash")) ? null : reader.GetString(reader.GetOrdinal("arguments_hash")),
            ArgumentsEvaluated = reader.GetBoolean(reader.GetOrdinal("arguments_evaluated")),
            CallOrigin = (AgentToolCallOrigin)reader.GetInt32(reader.GetOrdinal("call_origin")),
            AgentRolesHash = reader.IsDBNull(reader.GetOrdinal("agent_roles_hash")) ? null : reader.GetString(reader.GetOrdinal("agent_roles_hash")),
            ToolContract = DeserializeJson(reader, "tool_contract_json", PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolContractIdentity)!,
            CapabilityContract = DeserializeJson(reader, "capability_contract_json", PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolContractIdentity)!,
            InputSchemaContract = DeserializeJson(reader, "input_schema_contract_json", PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolSchemaContractIdentity),
            OutputSchemaContract = DeserializeJson(reader, "output_schema_contract_json", PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolSchemaContractIdentity),
            Governance = DeserializeJson(reader, "governance_json", PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolEffectiveGovernance)!
        };

        return new AgentToolGovernancePreDispatchRecord
        {
            Context = ctx,
            Lease = DeserializeJson(reader, "lease_json", PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationLease)!,
            Approval = DeserializeJson(reader, "approval_json", PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolApprovalResult)!,
            BudgetReservation = DeserializeJson(reader, "budget_reservation_json", PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolBudgetReservation)!
        };
    }

    private static T? DeserializeJson<T>(NpgsqlDataReader reader, string columnName, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal)) return default;
        return PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(ordinal), typeInfo);
    }
}
