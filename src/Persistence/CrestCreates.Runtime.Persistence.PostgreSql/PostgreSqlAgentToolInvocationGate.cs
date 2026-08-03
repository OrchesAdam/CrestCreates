using System.Text.Json;
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

    private async ValueTask AbandonCoreAsync(AgentToolInvocationLease lease, string reasonCode, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"update {_options.Schema}.agent_tool_invocation_pre_dispatch set pre_dispatch_state = {(int)AgentToolInvocationPreDispatchState.Abandoned}, updated_at = clock_timestamp() where lease_id = @lid";
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask<AgentToolInvocationAcquireResult> AcquireCoreAsync(AgentToolInvocationAcquireRequest req, CancellationToken ct)
    {
        var lease = new AgentToolInvocationLease
        {
            LeaseId = Guid.NewGuid().ToString("N"),
            AttemptId = Guid.NewGuid().ToString("N"),
            FencingToken = DateTimeOffset.UtcNow.Ticks,
            AcquiredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30)
        };
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            insert into {_options.Schema}.agent_tool_invocation_pre_dispatch
                (tenant_id, lease_id, attempt_id, fencing_token, acquired_at, expires_at, pre_dispatch_state)
            values (@tid, @lid, @aid, @ft, @aa, @ea, @st)
            on conflict do nothing
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tid", req.Key.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(new NpgsqlParameter("aa", lease.AcquiredAt));
        cmd.Parameters.Add(new NpgsqlParameter("ea", lease.ExpiresAt));
        cmd.Parameters.Add(new NpgsqlParameter("st", NpgsqlDbType.Integer) { Value = (int)AgentToolInvocationPreDispatchState.Unknown });
        await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationAcquireResult { Status = AgentToolInvocationAcquireStatus.Acquired, Lease = lease };
    }

    private async ValueTask<AgentToolInvocationLease> RenewCoreAsync(AgentToolInvocationLease lease, CancellationToken ct)
    {
        var renewed = lease with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30) };
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"update {_options.Schema}.agent_tool_invocation_pre_dispatch set expires_at = @ea, updated_at = clock_timestamp() where lease_id = @lid";
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("ea", renewed.ExpiresAt));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return renewed;
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> PrepareIntentCoreAsync(AgentToolInvocationLease lease, AgentToolInvocationPreparePreDispatchIntentRequest req, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st, intent_json = @ij::jsonb, updated_at = clock_timestamp()
            where lease_id = @lid and pre_dispatch_state = 0 returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("st", NpgsqlDbType.Integer) { Value = (int)AgentToolInvocationPreDispatchState.Pending });
        cmd.Parameters.Add(new NpgsqlParameter("ij", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(req.Intent) });
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationPreDispatchResult
        {
            State = r is not null ? AgentToolInvocationPreDispatchState.Pending : AgentToolInvocationPreDispatchState.Unknown,
            Intent = req.Intent
        };
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> BindReservationCoreAsync(AgentToolInvocationLease lease, AgentToolInvocationBindReservationRequest req, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st, bound_reservation_id = @rid, updated_at = clock_timestamp()
            where lease_id = @lid and pre_dispatch_state = @ps returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("st", (int)AgentToolInvocationPreDispatchState.Ready));
        cmd.Parameters.Add(new NpgsqlParameter("rid", req.ReservationId));
        cmd.Parameters.Add(new NpgsqlParameter("ps", (int)AgentToolInvocationPreDispatchState.Pending));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationPreDispatchResult
        {
            State = r is not null ? AgentToolInvocationPreDispatchState.Ready : AgentToolInvocationPreDispatchState.Unknown,
            BoundReservationId = req.ReservationId
        };
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedCoreAsync(AgentToolInvocationLease lease, AgentToolInvocationBindPreDispatchRequest req, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st, accepted_receipt_json = @rj::jsonb, updated_at = clock_timestamp()
            where lease_id = @lid and pre_dispatch_state = @ps returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("st", (int)AgentToolInvocationPreDispatchState.Accepted));
        cmd.Parameters.Add(new NpgsqlParameter("rj", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(req.Receipt) });
        cmd.Parameters.Add(new NpgsqlParameter("ps", (int)AgentToolInvocationPreDispatchState.Ready));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationPreDispatchResult
        {
            State = r is not null ? AgentToolInvocationPreDispatchState.Accepted : AgentToolInvocationPreDispatchState.Unknown,
            AcceptedReceipt = req.Receipt
        };
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateCoreAsync(AgentToolPreDispatchIdentity identity, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select pre_dispatch_state, bound_reservation_id
            from {_options.Schema}.agent_tool_invocation_pre_dispatch
            where tenant_id = @tid and attempt_id = @aid
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tid", identity.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("aid", identity.AttemptId));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return new AgentToolInvocationPreDispatchResult { State = AgentToolInvocationPreDispatchState.Unknown };
        return new AgentToolInvocationPreDispatchResult
        {
            State = (AgentToolInvocationPreDispatchState)reader.GetInt32(0),
            BoundReservationId = reader.IsDBNull(1) ? null : reader.GetString(1)
        };
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialCoreAsync(AgentToolInvocationLease lease, AgentToolInvocationPublishDenialRequest req, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st, updated_at = clock_timestamp()
            where lease_id = @lid and pre_dispatch_state in (0, 1)
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("st", (int)AgentToolInvocationPreDispatchState.Abandoned));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationPreDispatchResult
        {
            State = AgentToolInvocationPreDispatchState.Abandoned,
            ReasonCode = req.ReasonCode
        };
    }

    private async ValueTask<bool> TryMarkDispatchStartedCoreAsync(AgentToolInvocationLease lease, AgentToolGovernancePreDispatchReceipt receipt, string reservationId, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st, dispatch_started_at = clock_timestamp(), updated_at = clock_timestamp()
            where lease_id = @lid and pre_dispatch_state = @ps and bound_reservation_id = @rid returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("st", (int)AgentToolInvocationPreDispatchState.DispatchStarted));
        cmd.Parameters.Add(new NpgsqlParameter("ps", (int)AgentToolInvocationPreDispatchState.Accepted));
        cmd.Parameters.Add(new NpgsqlParameter("rid", reservationId));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return r is not null;
    }

    private async ValueTask PrepareCompletionCoreAsync(AgentToolInvocationLease lease, AgentToolInvocationPrepareCompletionRequest req, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"update {_options.Schema}.agent_tool_invocation_pre_dispatch set pre_dispatch_state = @st, updated_at = clock_timestamp() where lease_id = @lid";
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("st", (int)AgentToolInvocationPreDispatchState.CompletionPending));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask<AgentToolInvocationCompletionResult> PublishCompletionCoreAsync(AgentToolInvocationLease lease, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"update {_options.Schema}.agent_tool_invocation_pre_dispatch set pre_dispatch_state = @st, updated_at = clock_timestamp() where lease_id = @lid";
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("st", (int)AgentToolInvocationPreDispatchState.Completed));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationCompletionResult { State = AgentToolInvocationCompletionState.Completed };
    }

    private async ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateCoreAsync(AgentToolInvocationLease lease, CancellationToken ct)
    {
        await Task.CompletedTask;
        return new AgentToolInvocationCompletionResult { State = AgentToolInvocationCompletionState.Unknown };
    }

    private async ValueTask PrepareReleaseCoreAsync(AgentToolInvocationLease lease, AgentToolInvocationPrepareReleaseRequest req, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"update {_options.Schema}.agent_tool_invocation_pre_dispatch set pre_dispatch_state = @st, updated_at = clock_timestamp() where lease_id = @lid";
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("st", (int)AgentToolInvocationPreDispatchState.ReleasePending));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask<AgentToolInvocationReleaseResult> PublishReleaseCoreAsync(AgentToolInvocationLease lease, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"update {_options.Schema}.agent_tool_invocation_pre_dispatch set pre_dispatch_state = @st, updated_at = clock_timestamp() where lease_id = @lid";
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("st", (int)AgentToolInvocationPreDispatchState.Released));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return new AgentToolInvocationReleaseResult { State = AgentToolInvocationReleaseState.Released };
    }

    private async ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateCoreAsync(AgentToolInvocationLease lease, CancellationToken ct)
    {
        await Task.CompletedTask;
        return new AgentToolInvocationReleaseResult { State = AgentToolInvocationReleaseState.Unknown };
    }

    private async ValueTask MarkIndeterminateCoreAsync(AgentToolInvocationLease lease, string reasonCode, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"update {_options.Schema}.agent_tool_invocation_pre_dispatch set pre_dispatch_state = @st, updated_at = clock_timestamp() where lease_id = @lid";
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("st", (int)AgentToolInvocationPreDispatchState.Indeterminate));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
