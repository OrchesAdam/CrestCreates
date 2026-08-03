using CrestCreates.Agent.Tools;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlAgentToolInvocationGate : IAgentToolInvocationGate
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;

    public PostgreSqlAgentToolInvocationGate(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public ValueTask AbandonUnrecordedLeaseAsync(AgentToolInvocationLease lease, string reasonCode, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => AbandonCoreAsync(lease, reasonCode, ct), cancellationToken);

    public ValueTask<AgentToolInvocationAcquireResult> AcquireAsync(AgentToolInvocationAcquireRequest request, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => AcquireCoreAsync(request, ct), cancellationToken);

    public ValueTask<AgentToolInvocationLease> RenewAsync(AgentToolInvocationLease lease, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => RenewCoreAsync(lease, ct), cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> PreparePreDispatchIntentAsync(AgentToolInvocationLease lease, AgentToolInvocationPreparePreDispatchIntentRequest request, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => PrepareIntentCoreAsync(lease, request, ct), cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> BindPreDispatchReservationAsync(AgentToolInvocationLease lease, AgentToolInvocationBindReservationRequest request, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => BindReservationCoreAsync(lease, request, ct), cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedPreDispatchAsync(AgentToolInvocationLease lease, AgentToolInvocationBindPreDispatchRequest request, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => BindAcceptedCoreAsync(lease, request, ct), cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetPreDispatchStateCoreAsync(identity, ct), cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialAsync(AgentToolInvocationLease lease, AgentToolInvocationPublishDenialRequest request, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => PublishBudgetDenialCoreAsync(lease, request, ct), cancellationToken);

    public ValueTask<bool> TryMarkDispatchStartedAsync(AgentToolInvocationLease lease, AgentToolGovernancePreDispatchReceipt receipt, string reservationId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => TryMarkDispatchStartedCoreAsync(lease, receipt, reservationId, ct), cancellationToken);

    public ValueTask PrepareCompletionAsync(AgentToolInvocationLease lease, AgentToolInvocationPrepareCompletionRequest request, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => PrepareCompletionCoreAsync(lease, request, ct), cancellationToken);

    public ValueTask<AgentToolInvocationCompletionResult> PublishCompletionAsync(AgentToolInvocationLease lease, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => PublishCompletionCoreAsync(lease, ct), cancellationToken);

    public ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateAsync(AgentToolInvocationLease lease, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetCompletionStateCoreAsync(lease, ct), cancellationToken);

    public ValueTask PrepareReleaseAsync(AgentToolInvocationLease lease, AgentToolInvocationPrepareReleaseRequest request, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => PrepareReleaseCoreAsync(lease, request, ct), cancellationToken);

    public ValueTask<AgentToolInvocationReleaseResult> PublishReleaseAsync(AgentToolInvocationLease lease, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => PublishReleaseCoreAsync(lease, ct), cancellationToken);

    public ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateAsync(AgentToolInvocationLease lease, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetReleaseStateCoreAsync(lease, ct), cancellationToken);

    public ValueTask MarkIndeterminateAsync(AgentToolInvocationLease lease, string reasonCode, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => MarkIndeterminateCoreAsync(lease, reasonCode, ct), cancellationToken);

    private NpgsqlConnection Conn() => _coordinator.RequireSession().Connection;

    private static NpgsqlParameter IntParam(string name, int value)
        => new NpgsqlParameter(name, NpgsqlDbType.Integer) { Value = value };

    private static NpgsqlParameter JsonParam(string name, string json)
        => new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = json };

    private async ValueTask AbandonCoreAsync(AgentToolInvocationLease lease, string reasonCode, CancellationToken ct)
    {
        var abandonedReceipt = new AgentToolInvocationAbandonedReceipt
        {
            Identity = new AgentToolPreDispatchIdentity(
                new AgentToolLogicalInvocationKey(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty),
                lease.AttemptId),
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.GovernanceDenied,
                Code = reasonCode,
                Message = reasonCode
            },
            ReasonCode = reasonCode,
            AbandonedAt = DateTimeOffset.UtcNow
        };
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                abandoned_receipt_json = @arj,
                updated_at = clock_timestamp()
            where lease_id = @lid and pre_dispatch_state in (0, 1)
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Abandoned));
        cmd.Parameters.Add(JsonParam("arj", PostgreSqlRuntimeStoreSupport.Serialize(abandonedReceipt, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationAbandonedReceipt)));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask<AgentToolInvocationAcquireResult> AcquireCoreAsync(AgentToolInvocationAcquireRequest req, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var lease = new AgentToolInvocationLease
        {
            LeaseId = Guid.NewGuid().ToString("N"),
            AttemptId = Guid.NewGuid().ToString("N"),
            FencingToken = now.Ticks,
            AcquiredAt = now,
            ExpiresAt = now.AddSeconds(30)
        };
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            insert into {_options.Schema}.agent_tool_invocation_pre_dispatch
                (tenant_id, lease_id, attempt_id, logical_invocation_key, invocation_fingerprint,
                 fencing_token, acquired_at, expires_at, pre_dispatch_state, revision)
            values (@tid, @lid, @aid, @lik, @fp, @ft, @aa, @ea, @st, 1)
            on conflict (tenant_id, attempt_id) do nothing
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tid", req.Key.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(JsonParam("lik", PostgreSqlRuntimeStoreSupport.Serialize(req.Key, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey)));
        cmd.Parameters.Add(new NpgsqlParameter("fp", req.InvocationFingerprint ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(new NpgsqlParameter("aa", lease.AcquiredAt));
        cmd.Parameters.Add(new NpgsqlParameter("ea", lease.ExpiresAt));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Unknown));
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null)
            return new AgentToolInvocationAcquireResult { Status = AgentToolInvocationAcquireStatus.Conflict };
        return new AgentToolInvocationAcquireResult { Status = AgentToolInvocationAcquireStatus.Acquired, Lease = lease };
    }

    private async ValueTask<AgentToolInvocationLease> RenewCoreAsync(AgentToolInvocationLease lease, CancellationToken ct)
    {
        var renewed = lease with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30) };
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set expires_at = @ea, updated_at = clock_timestamp()
            where lease_id = @lid
              and pre_dispatch_state not in (
                {(int)AgentToolInvocationPreDispatchState.Released},
                {(int)AgentToolInvocationPreDispatchState.Completed},
                {(int)AgentToolInvocationPreDispatchState.Abandoned})
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("ea", renewed.ExpiresAt));
        var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (rows == 0)
            throw new InvalidOperationException("Lease renewal failed — lease is terminal or does not exist.");
        return renewed;
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> PrepareIntentCoreAsync(
        AgentToolInvocationLease lease, AgentToolInvocationPreparePreDispatchIntentRequest req, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                intent_json = @ij,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and pre_dispatch_state in (0, {(int)AgentToolInvocationPreDispatchState.Unknown})
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Pending));
        cmd.Parameters.Add(JsonParam("ij", PostgreSqlRuntimeStoreSupport.Serialize(req.Intent, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationPreDispatchIntentSnapshot)));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationPreDispatchResult
        {
            State = r is not null ? AgentToolInvocationPreDispatchState.Pending : AgentToolInvocationPreDispatchState.Unknown,
            Intent = req.Intent
        };
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> BindReservationCoreAsync(
        AgentToolInvocationLease lease, AgentToolInvocationBindReservationRequest req, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                bound_reservation_id = @rid,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid and pre_dispatch_state = @ps
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Ready));
        cmd.Parameters.Add(new NpgsqlParameter("rid", req.ReservationId));
        cmd.Parameters.Add(IntParam("ps", (int)AgentToolInvocationPreDispatchState.Pending));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationPreDispatchResult
        {
            State = r is not null ? AgentToolInvocationPreDispatchState.Ready : AgentToolInvocationPreDispatchState.Unknown,
            BoundReservationId = req.ReservationId
        };
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedCoreAsync(
        AgentToolInvocationLease lease, AgentToolInvocationBindPreDispatchRequest req, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                accepted_receipt_json = @rj,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid and pre_dispatch_state = @ps
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Accepted));
        cmd.Parameters.Add(JsonParam("rj", PostgreSqlRuntimeStoreSupport.Serialize(req.Receipt, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolGovernancePreDispatchReceipt)));
        cmd.Parameters.Add(IntParam("ps", (int)AgentToolInvocationPreDispatchState.Ready));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationPreDispatchResult
        {
            State = r is not null ? AgentToolInvocationPreDispatchState.Accepted : AgentToolInvocationPreDispatchState.Unknown,
            AcceptedReceipt = req.Receipt
        };
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateCoreAsync(
        AgentToolPreDispatchIdentity identity, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select pre_dispatch_state, bound_reservation_id, accepted_receipt_json, abandoned_receipt_json
            from {_options.Schema}.agent_tool_invocation_pre_dispatch
            where tenant_id = @tid
              and attempt_id = @aid
              and logical_invocation_key = @lik
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tid", identity.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("aid", identity.AttemptId));
        cmd.Parameters.Add(JsonParam("lik", PostgreSqlRuntimeStoreSupport.Serialize(identity.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey)));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return new AgentToolInvocationPreDispatchResult { State = AgentToolInvocationPreDispatchState.Unknown };
        var state = (AgentToolInvocationPreDispatchState)reader.GetInt32(0);
        var boundReservationId = reader.IsDBNull(1) ? null : reader.GetString(1);
        AgentToolGovernancePreDispatchReceipt? acceptedReceipt = null;
        AgentToolInvocationAbandonedReceipt? abandonedReceipt = null;
        if (!reader.IsDBNull(2))
            acceptedReceipt = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(2), PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolGovernancePreDispatchReceipt);
        if (!reader.IsDBNull(3))
            abandonedReceipt = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(3), PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationAbandonedReceipt);
        return new AgentToolInvocationPreDispatchResult
        {
            State = state,
            BoundReservationId = boundReservationId,
            AcceptedReceipt = acceptedReceipt,
            AbandonedReceipt = abandonedReceipt
        };
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialCoreAsync(
        AgentToolInvocationLease lease, AgentToolInvocationPublishDenialRequest req, CancellationToken ct)
    {
        var abandonedReceipt = new AgentToolInvocationAbandonedReceipt
        {
            Identity = new AgentToolPreDispatchIdentity(
                new AgentToolLogicalInvocationKey(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty),
                lease.AttemptId),
            Outcome = req.Outcome,
            ReasonCode = req.ReasonCode,
            AbandonedAt = DateTimeOffset.UtcNow
        };
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                abandoned_receipt_json = @arj,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and pre_dispatch_state in (0, {(int)AgentToolInvocationPreDispatchState.Unknown}, {(int)AgentToolInvocationPreDispatchState.Pending})
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Abandoned));
        cmd.Parameters.Add(JsonParam("arj", PostgreSqlRuntimeStoreSupport.Serialize(abandonedReceipt, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationAbandonedReceipt)));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationPreDispatchResult
        {
            State = r is not null ? AgentToolInvocationPreDispatchState.Abandoned : AgentToolInvocationPreDispatchState.Unknown,
            AbandonedReceipt = r is not null ? abandonedReceipt : null,
            ReasonCode = req.ReasonCode
        };
    }

    private async ValueTask<bool> TryMarkDispatchStartedCoreAsync(
        AgentToolInvocationLease lease, AgentToolGovernancePreDispatchReceipt receipt, string reservationId, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                dispatch_started_at = clock_timestamp(),
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and pre_dispatch_state = @ps
              and bound_reservation_id = @rid
              and accepted_receipt_json->>'AuditId' = @auditId
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.DispatchStarted));
        cmd.Parameters.Add(IntParam("ps", (int)AgentToolInvocationPreDispatchState.Accepted));
        cmd.Parameters.Add(new NpgsqlParameter("rid", reservationId));
        cmd.Parameters.Add(new NpgsqlParameter("auditId", receipt.AuditId));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return r is not null;
    }

    private async ValueTask PrepareCompletionCoreAsync(
        AgentToolInvocationLease lease, AgentToolInvocationPrepareCompletionRequest req, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                completion_outcome_json = @coj,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid and pre_dispatch_state = @ps
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.CompletionPending));
        cmd.Parameters.Add(IntParam("ps", (int)AgentToolInvocationPreDispatchState.DispatchStarted));
        cmd.Parameters.Add(JsonParam("coj", PostgreSqlRuntimeStoreSupport.Serialize(req, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationPrepareCompletionRequest)));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask<AgentToolInvocationCompletionResult> PublishCompletionCoreAsync(
        AgentToolInvocationLease lease, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid and pre_dispatch_state = @ps
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Completed));
        cmd.Parameters.Add(IntParam("ps", (int)AgentToolInvocationPreDispatchState.CompletionPending));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationCompletionResult
        {
            State = r is not null ? AgentToolInvocationCompletionState.Completed : AgentToolInvocationCompletionState.Unknown
        };
    }

    private async ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateCoreAsync(
        AgentToolInvocationLease lease, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select pre_dispatch_state
            from {_options.Schema}.agent_tool_invocation_pre_dispatch
            where lease_id = @lid
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return new AgentToolInvocationCompletionResult { State = AgentToolInvocationCompletionState.Unknown };
        var state = (AgentToolInvocationPreDispatchState)reader.GetInt32(0);
        return new AgentToolInvocationCompletionResult
        {
            State = state == AgentToolInvocationPreDispatchState.Completed
                ? AgentToolInvocationCompletionState.Completed
                : AgentToolInvocationCompletionState.Unknown
        };
    }

    private async ValueTask PrepareReleaseCoreAsync(
        AgentToolInvocationLease lease, AgentToolInvocationPrepareReleaseRequest req, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                release_outcome_json = @roj,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid and pre_dispatch_state = @ps
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.ReleasePending));
        cmd.Parameters.Add(IntParam("ps", (int)AgentToolInvocationPreDispatchState.Accepted));
        cmd.Parameters.Add(JsonParam("roj", PostgreSqlRuntimeStoreSupport.Serialize(req, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationPrepareReleaseRequest)));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask<AgentToolInvocationReleaseResult> PublishReleaseCoreAsync(
        AgentToolInvocationLease lease, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid and pre_dispatch_state = @ps
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Released));
        cmd.Parameters.Add(IntParam("ps", (int)AgentToolInvocationPreDispatchState.ReleasePending));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationReleaseResult
        {
            State = r is not null ? AgentToolInvocationReleaseState.Released : AgentToolInvocationReleaseState.Unknown
        };
    }

    private async ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateCoreAsync(
        AgentToolInvocationLease lease, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select pre_dispatch_state
            from {_options.Schema}.agent_tool_invocation_pre_dispatch
            where lease_id = @lid
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return new AgentToolInvocationReleaseResult { State = AgentToolInvocationReleaseState.Unknown };
        var state = (AgentToolInvocationPreDispatchState)reader.GetInt32(0);
        return new AgentToolInvocationReleaseResult
        {
            State = state == AgentToolInvocationPreDispatchState.Released
                ? AgentToolInvocationReleaseState.Released
                : AgentToolInvocationReleaseState.Unknown
        };
    }

    private async ValueTask MarkIndeterminateCoreAsync(AgentToolInvocationLease lease, string reasonCode, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Indeterminate));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
