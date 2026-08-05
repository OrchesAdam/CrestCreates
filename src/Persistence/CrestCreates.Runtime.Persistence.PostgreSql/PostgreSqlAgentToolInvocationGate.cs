using CrestCreates.Agent.Tools;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlAgentToolInvocationGate : IAgentToolInvocationGate, IAgentToolInvocationLeaseAbandoner, IAgentToolPreDispatchPersistenceCapabilities
{
    public AgentToolPreDispatchPersistenceCapability Capability => AgentToolPreDispatchPersistenceCapability.FullDurable;

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

    public ValueTask<AgentToolInvocationPreDispatchResult> ReleaseByIdentityAsync(
        AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => ReleaseByIdentityCoreAsync(identity, reasonCode, ct), cancellationToken);

    public ValueTask<AgentToolInvocationPreDispatchResult> AbandonByIdentityAsync(
        AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => AbandonByIdentityCoreAsync(identity, reasonCode, ct), cancellationToken);

    private NpgsqlConnection Conn() => _coordinator.RequireSession().Connection;

    private static NpgsqlParameter IntParam(string name, int value)
        => new NpgsqlParameter(name, NpgsqlDbType.Integer) { Value = value };

    private static NpgsqlParameter JsonParam(string name, string json)
        => new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = json };

    private async ValueTask AbandonCoreAsync(AgentToolInvocationLease lease, string reasonCode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        AgentToolLogicalInvocationKey logicalKey;
        AgentToolInvocationPreDispatchState currentState;
        AgentToolInvocationAbandonedReceipt? existingReceipt;
        DateTimeOffset expiresAt;
        await using var readCmd = Conn().CreateCommand();
        readCmd.CommandText = $"""
            select logical_invocation_key, pre_dispatch_state, abandoned_receipt_json, expires_at
            from {_options.Schema}.agent_tool_invocation_pre_dispatch
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
            """;
        readCmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        readCmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        readCmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        await using (var reader = await readCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("The invocation lease is stale or unknown.");

            logicalKey = PostgreSqlRuntimeStoreSupport.Deserialize(
                reader.GetString(0),
                PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey);
            currentState = (AgentToolInvocationPreDispatchState)reader.GetInt32(1);
            existingReceipt = reader.IsDBNull(2)
                ? null
                : PostgreSqlRuntimeStoreSupport.Deserialize(
                    reader.GetString(2),
                    PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationAbandonedReceipt);
            expiresAt = reader.GetFieldValue<DateTimeOffset>(3);
        }

        if (currentState == AgentToolInvocationPreDispatchState.Abandoned)
        {
            if (existingReceipt is null
                || !string.Equals(existingReceipt.ReasonCode, reasonCode, StringComparison.Ordinal))
                throw new InvalidOperationException("The abandoned lease reason cannot be changed.");
            return;
        }

        if (currentState is not (AgentToolInvocationPreDispatchState.Unknown or AgentToolInvocationPreDispatchState.Pending))
            throw new InvalidOperationException("The invocation attempt can no longer be abandoned.");
        if (expiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The invocation lease has expired.");

        var abandonedReceipt = new AgentToolInvocationAbandonedReceipt
        {
            Identity = new AgentToolPreDispatchIdentity(logicalKey, lease.AttemptId),
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
                last_reason_code = @rc,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
              and expires_at > @now
              and pre_dispatch_state in (
                  {(int)AgentToolInvocationPreDispatchState.Unknown},
                  {(int)AgentToolInvocationPreDispatchState.Pending})
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(new NpgsqlParameter("now", DateTimeOffset.UtcNow));
        cmd.Parameters.Add(new NpgsqlParameter("rc", reasonCode));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Abandoned));
        cmd.Parameters.Add(JsonParam("arj", PostgreSqlRuntimeStoreSupport.Serialize(abandonedReceipt, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationAbandonedReceipt)));
        if (await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new InvalidOperationException("The invocation lease changed while it was being abandoned.");
    }

    private async ValueTask<AgentToolInvocationAcquireResult> AcquireCoreAsync(AgentToolInvocationAcquireRequest req, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var logicalKeyJson = PostgreSqlRuntimeStoreSupport.Serialize(
            req.Key, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey);

        // Read the latest Attempt regardless of lease expiry. Pending/Ready/Accepted
        // are durable fences, so expiry can never turn them into an absent Attempt.
        string? expiredUnpreparedLeaseId = null;
        await using var checkCmd = Conn().CreateCommand();
        checkCmd.CommandText = $"""
            select lease_id, pre_dispatch_state, invocation_fingerprint, expires_at
            from {_options.Schema}.agent_tool_invocation_pre_dispatch
            where tenant_id = @tid
              and logical_invocation_key = @lik
            order by fencing_token desc
            limit 1
            """;
        checkCmd.Parameters.Add(new NpgsqlParameter("tid", req.Key.TenantId ?? string.Empty));
        checkCmd.Parameters.Add(JsonParam("lik", logicalKeyJson));
        await using var reader = await checkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var existingLeaseId = reader.GetString(0);
            var existingState = (AgentToolInvocationPreDispatchState)reader.GetInt32(1);
            var existingFingerprint = reader.GetString(2);
            var existingExpiry = reader.GetFieldValue<DateTimeOffset>(3);

            if (!string.Equals(existingFingerprint, req.InvocationFingerprint, StringComparison.Ordinal))
                return new AgentToolInvocationAcquireResult { Status = AgentToolInvocationAcquireStatus.Conflict };

            if (existingState == AgentToolInvocationPreDispatchState.Completed)
                return new AgentToolInvocationAcquireResult { Status = AgentToolInvocationAcquireStatus.Completed };
            if (existingState == AgentToolInvocationPreDispatchState.Indeterminate)
                return new AgentToolInvocationAcquireResult { Status = AgentToolInvocationAcquireStatus.Indeterminate };
            if (existingState is AgentToolInvocationPreDispatchState.Pending
                or AgentToolInvocationPreDispatchState.Ready
                or AgentToolInvocationPreDispatchState.Accepted
                or AgentToolInvocationPreDispatchState.ReleasePending
                or AgentToolInvocationPreDispatchState.CompletionPending)
                return new AgentToolInvocationAcquireResult { Status = AgentToolInvocationAcquireStatus.InProgress };
            if (existingState == AgentToolInvocationPreDispatchState.DispatchStarted)
                return new AgentToolInvocationAcquireResult
                {
                    Status = existingExpiry > now
                        ? AgentToolInvocationAcquireStatus.InProgress
                        : AgentToolInvocationAcquireStatus.Indeterminate
                };
            if (existingState == AgentToolInvocationPreDispatchState.Unknown
                && existingExpiry > now)
                return new AgentToolInvocationAcquireResult { Status = AgentToolInvocationAcquireStatus.InProgress };
            if (existingState == AgentToolInvocationPreDispatchState.Unknown)
                expiredUnpreparedLeaseId = existingLeaseId;
        }
        await reader.CloseAsync().ConfigureAwait(false);

        if (expiredUnpreparedLeaseId is not null)
        {
            await using var expireCmd = Conn().CreateCommand();
            expireCmd.CommandText = $"""
                update {_options.Schema}.agent_tool_invocation_pre_dispatch
                set pre_dispatch_state = @abandoned,
                    last_reason_code = @reason,
                    revision = revision + 1,
                    updated_at = clock_timestamp()
                where tenant_id = @tid
                  and lease_id = @lid
                  and pre_dispatch_state = @unknown
                  and expires_at <= @now
                """;
            expireCmd.Parameters.Add(new NpgsqlParameter("tid", req.Key.TenantId ?? string.Empty));
            expireCmd.Parameters.Add(new NpgsqlParameter("lid", expiredUnpreparedLeaseId));
            expireCmd.Parameters.Add(IntParam("abandoned", (int)AgentToolInvocationPreDispatchState.Abandoned));
            expireCmd.Parameters.Add(IntParam("unknown", (int)AgentToolInvocationPreDispatchState.Unknown));
            expireCmd.Parameters.Add(new NpgsqlParameter("reason", "lease_expired_before_pre_dispatch"));
            expireCmd.Parameters.Add(new NpgsqlParameter("now", now));
            if (await expireCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
                return new AgentToolInvocationAcquireResult { Status = AgentToolInvocationAcquireStatus.InProgress };
        }

        // Acquire a monotonic fencing token from the database sequence.
        long fencingToken;
        await using (var seqCmd = Conn().CreateCommand())
        {
            seqCmd.CommandText = $"select nextval('{_options.Schema}.agent_tool_fencing_token_seq')";
            fencingToken = (long)(await seqCmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L);
        }

        var lease = new AgentToolInvocationLease
        {
            LeaseId = Guid.NewGuid().ToString("N"),
            AttemptId = Guid.NewGuid().ToString("N"),
            FencingToken = fencingToken,
            AcquiredAt = now,
            ExpiresAt = now.AddSeconds(30)
        };
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            insert into {_options.Schema}.agent_tool_invocation_pre_dispatch
                (tenant_id, lease_id, attempt_id, logical_invocation_key, invocation_fingerprint,
                 fencing_token, acquired_at, expires_at, pre_dispatch_state, revision)
            values (@tid, @lid, @aid, @lik, @fp, @ft, @aa, @ea, @st, 1)
            on conflict (tenant_id, logical_invocation_key) where pre_dispatch_state in (0, 1, 2, 3, 4, 6, 8, 10) do nothing
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tid", req.Key.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(JsonParam("lik", logicalKeyJson));
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
        var now = DateTimeOffset.UtcNow;
        var renewed = lease with { ExpiresAt = now.AddSeconds(30) };
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set expires_at = @ea, updated_at = clock_timestamp()
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
              and expires_at = @expectedExpiry
              and expires_at > @now
              and pre_dispatch_state not in (
                {(int)AgentToolInvocationPreDispatchState.Released},
                {(int)AgentToolInvocationPreDispatchState.Completed},
                {(int)AgentToolInvocationPreDispatchState.Abandoned})
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(new NpgsqlParameter("expectedExpiry", lease.ExpiresAt));
        cmd.Parameters.Add(new NpgsqlParameter("now", now));
        cmd.Parameters.Add(new NpgsqlParameter("ea", renewed.ExpiresAt));
        var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (rows == 0)
            throw new InvalidOperationException("Lease renewal failed — lease is terminal or does not exist.");
        return renewed;
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> PrepareIntentCoreAsync(
        AgentToolInvocationLease lease, AgentToolInvocationPreparePreDispatchIntentRequest req, CancellationToken ct)
    {
        if (req.Intent.FrozenLease != lease
            || !string.Equals(req.Intent.Context.AttemptId, lease.AttemptId, StringComparison.Ordinal)
            || !string.Equals(
                req.Intent.Context.InvocationFingerprint,
                req.Intent.InvocationFingerprint,
                StringComparison.Ordinal))
        {
            return new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Unknown,
                ReasonCode = "pre_dispatch_intent_conflict"
            };
        }

        var intentJson = PostgreSqlRuntimeStoreSupport.Serialize(req.Intent, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationPreDispatchIntentSnapshot);
        var logicalKeyJson = PostgreSqlRuntimeStoreSupport.Serialize(
            req.Intent.Context.LogicalInvocationKey,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey);
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                intent_json = @ij,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
              and logical_invocation_key = @lik
              and invocation_fingerprint = @fp
              and expires_at > @now
              and pre_dispatch_state = {(int)AgentToolInvocationPreDispatchState.Unknown}
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(JsonParam("lik", logicalKeyJson));
        cmd.Parameters.Add(new NpgsqlParameter("fp", req.Intent.InvocationFingerprint));
        cmd.Parameters.Add(new NpgsqlParameter("now", DateTimeOffset.UtcNow));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Pending));
        cmd.Parameters.Add(JsonParam("ij", intentJson));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (r is not null)
            return new AgentToolInvocationPreDispatchResult { State = AgentToolInvocationPreDispatchState.Pending, Intent = req.Intent };

        // Idempotent retry: read current state and verify content matches.
        var current = await ReadCurrentStateAsync(lease, ct);
        return current.State == AgentToolInvocationPreDispatchState.Pending
            && current.Intent is not null
            && AgentToolGovernancePreDispatchComparer.Equivalent(current.Intent, req.Intent)
                ? current
                : new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = "pre_dispatch_intent_conflict"
                };
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> BindReservationCoreAsync(
        AgentToolInvocationLease lease, AgentToolInvocationBindReservationRequest req, CancellationToken ct)
    {
        var before = await ReadCurrentStateAsync(lease, ct);
        if (!string.Equals(req.ReservationId, req.Reservation.ReservationId, StringComparison.Ordinal)
            || !string.Equals(req.Reservation.AttemptId, lease.AttemptId, StringComparison.Ordinal)
            || before.Intent is null
            || !string.Equals(
                before.Intent.InvocationFingerprint,
                req.Reservation.InvocationFingerprint,
                StringComparison.Ordinal))
        {
            return new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Unknown,
                ReasonCode = "pre_dispatch_reservation_conflict"
            };
        }

        var reservationJson = PostgreSqlRuntimeStoreSupport.Serialize(
            req.Reservation,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolBudgetReservation);
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                bound_reservation_id = @rid,
                bound_reservation_json = @rj,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
              and expires_at > @now
              and pre_dispatch_state = {(int)AgentToolInvocationPreDispatchState.Pending}
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(new NpgsqlParameter("now", DateTimeOffset.UtcNow));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Ready));
        cmd.Parameters.Add(new NpgsqlParameter("rid", req.ReservationId));
        cmd.Parameters.Add(JsonParam("rj", reservationJson));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (r is not null)
            return new AgentToolInvocationPreDispatchResult { State = AgentToolInvocationPreDispatchState.Ready, BoundReservationId = req.ReservationId };

        // Idempotent retry: read current state and verify content matches.
        var current = await ReadCurrentStateAsync(lease, ct);
        var storedReservation = current.State == AgentToolInvocationPreDispatchState.Ready
            ? await ReadBoundReservationAsync(lease, ct)
            : null;
        return current.State == AgentToolInvocationPreDispatchState.Ready
            && string.Equals(current.BoundReservationId, req.ReservationId, StringComparison.Ordinal)
            && storedReservation is not null
            && AgentToolGovernancePreDispatchComparer.ReservationIdentityAndTermsEqual(
                storedReservation,
                req.Reservation)
            && storedReservation.State == req.Reservation.State
                ? current
                : new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = "pre_dispatch_reservation_conflict"
                };
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedCoreAsync(
        AgentToolInvocationLease lease, AgentToolInvocationBindPreDispatchRequest req, CancellationToken ct)
    {
        var before = await ReadCurrentStateAsync(lease, ct);
        if (before.Intent is null
            || !string.Equals(
                req.Receipt.Identity.AttemptId,
                lease.AttemptId,
                StringComparison.Ordinal)
            || req.Receipt.Identity.LogicalInvocationKey
                != before.Intent.Context.LogicalInvocationKey)
        {
            return new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Unknown,
                ReasonCode = "pre_dispatch_receipt_conflict"
            };
        }

        var receiptJson = PostgreSqlRuntimeStoreSupport.Serialize(req.Receipt, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolGovernancePreDispatchReceipt);
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                accepted_receipt_json = @rj,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
              and expires_at > @now
              and pre_dispatch_state = {(int)AgentToolInvocationPreDispatchState.Ready}
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(new NpgsqlParameter("now", DateTimeOffset.UtcNow));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Accepted));
        cmd.Parameters.Add(JsonParam("rj", receiptJson));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (r is not null)
            return new AgentToolInvocationPreDispatchResult { State = AgentToolInvocationPreDispatchState.Accepted, AcceptedReceipt = req.Receipt };

        // Idempotent retry: read current state and verify content matches.
        var current = await ReadCurrentStateAsync(lease, ct);
        return current.State == AgentToolInvocationPreDispatchState.Accepted
            && current.AcceptedReceipt is not null
            && AgentToolGovernancePreDispatchComparer.Equivalent(
                current.AcceptedReceipt,
                req.Receipt)
                ? current
                : new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = "pre_dispatch_receipt_conflict"
                };
    }

    private async ValueTask<GateRow> ReadGateRowAsync(
        AgentToolInvocationLease lease, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select pre_dispatch_state, bound_reservation_id, accepted_receipt_json,
                   abandoned_receipt_json, intent_json, last_reason_code,
                   indeterminate_at, indeterminate_reason,
                   completion_outcome_json, completion_prepared_at,
                   release_outcome_json, release_prepared_at
            from {_options.Schema}.agent_tool_invocation_pre_dispatch
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return new GateRow(AgentToolInvocationPreDispatchState.Unknown);
        return ReadGateRow(reader);
    }

    private static GateRow ReadGateRow(System.Data.Common.DbDataReader reader)
    {
        var state = (AgentToolInvocationPreDispatchState)reader.GetInt32(0);
        var boundReservationId = reader.IsDBNull(1) ? null : reader.GetString(1);
        AgentToolGovernancePreDispatchReceipt? acceptedReceipt = null;
        AgentToolInvocationAbandonedReceipt? abandonedReceipt = null;
        AgentToolInvocationPreDispatchIntentSnapshot? intent = null;
        if (!reader.IsDBNull(2))
            acceptedReceipt = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(2), PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolGovernancePreDispatchReceipt);
        if (!reader.IsDBNull(3))
            abandonedReceipt = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(3), PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationAbandonedReceipt);
        if (!reader.IsDBNull(4))
            intent = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(4), PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationPreDispatchIntentSnapshot);
        return new GateRow(
            state,
            boundReservationId,
            acceptedReceipt,
            abandonedReceipt,
            intent,
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11));
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> ReadCurrentStateAsync(
        AgentToolInvocationLease lease, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select pre_dispatch_state, bound_reservation_id, accepted_receipt_json,
                   abandoned_receipt_json, intent_json, last_reason_code
            from {_options.Schema}.agent_tool_invocation_pre_dispatch
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return new AgentToolInvocationPreDispatchResult { State = AgentToolInvocationPreDispatchState.Unknown };
        var state = (AgentToolInvocationPreDispatchState)reader.GetInt32(0);
        var boundReservationId = reader.IsDBNull(1) ? null : reader.GetString(1);
        AgentToolGovernancePreDispatchReceipt? acceptedReceipt = null;
        AgentToolInvocationAbandonedReceipt? abandonedReceipt = null;
        AgentToolInvocationPreDispatchIntentSnapshot? intent = null;
        if (!reader.IsDBNull(2))
            acceptedReceipt = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(2), PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolGovernancePreDispatchReceipt);
        if (!reader.IsDBNull(3))
            abandonedReceipt = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(3), PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationAbandonedReceipt);
        if (!reader.IsDBNull(4))
            intent = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(4), PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationPreDispatchIntentSnapshot);
        return new AgentToolInvocationPreDispatchResult
        {
            State = state,
            Intent = intent,
            BoundReservationId = boundReservationId,
            AcceptedReceipt = acceptedReceipt,
            AbandonedReceipt = abandonedReceipt,
            ReasonCode = reader.IsDBNull(5) ? null : reader.GetString(5)
        };
    }

    private async ValueTask<AgentToolBudgetReservation?> ReadBoundReservationAsync(
        AgentToolInvocationLease lease,
        CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select bound_reservation_json
            from {_options.Schema}.agent_tool_invocation_pre_dispatch
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        var json = (string?)await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return json is null
            ? null
            : PostgreSqlRuntimeStoreSupport.Deserialize(
                json,
                PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolBudgetReservation);
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateCoreAsync(
        AgentToolPreDispatchIdentity identity, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select pre_dispatch_state, bound_reservation_id, accepted_receipt_json,
                   abandoned_receipt_json, intent_json, lease_id, attempt_id,
                   fencing_token, acquired_at, expires_at, last_reason_code, indeterminate_at
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
        AgentToolInvocationPreDispatchIntentSnapshot? intent = null;
        if (!reader.IsDBNull(2))
            acceptedReceipt = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(2), PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolGovernancePreDispatchReceipt);
        if (!reader.IsDBNull(3))
            abandonedReceipt = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(3), PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationAbandonedReceipt);
        if (!reader.IsDBNull(4))
            intent = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(4), PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationPreDispatchIntentSnapshot);
        return new AgentToolInvocationPreDispatchResult
        {
            State = state,
            BoundReservationId = boundReservationId,
            AcceptedReceipt = acceptedReceipt,
            AbandonedReceipt = abandonedReceipt,
            Intent = intent,
            ReasonCode = reader.IsDBNull(10) ? null : reader.GetString(10),
            Indeterminate = !reader.IsDBNull(11)
        };
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialCoreAsync(
        AgentToolInvocationLease lease, AgentToolInvocationPublishDenialRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(req.Outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(req.ReasonCode);

        var current = await ReadCurrentStateAsync(lease, ct).ConfigureAwait(false);
        if (current.State == AgentToolInvocationPreDispatchState.Abandoned)
        {
            return current.AbandonedReceipt is not null
                && DenialEquals(current.AbandonedReceipt, req)
                ? current
                : new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = "pre_dispatch_denial_conflict"
                };
        }

        if (current.State != AgentToolInvocationPreDispatchState.Pending
            || current.Intent is null)
        {
            return new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Unknown,
                ReasonCode = "pre_dispatch_not_pending"
            };
        }

        var abandonedReceipt = new AgentToolInvocationAbandonedReceipt
        {
            Identity = new AgentToolPreDispatchIdentity(
                current.Intent.Context.LogicalInvocationKey,
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
                last_reason_code = @rc,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
              and expires_at > @now
              and pre_dispatch_state = {(int)AgentToolInvocationPreDispatchState.Pending}
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(new NpgsqlParameter("now", DateTimeOffset.UtcNow));
        cmd.Parameters.Add(new NpgsqlParameter("rc", req.ReasonCode));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Abandoned));
        cmd.Parameters.Add(JsonParam("arj", PostgreSqlRuntimeStoreSupport.Serialize(abandonedReceipt, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationAbandonedReceipt)));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (r is null)
        {
            current = await ReadCurrentStateAsync(lease, ct).ConfigureAwait(false);
            if (current.State == AgentToolInvocationPreDispatchState.Abandoned
                && current.AbandonedReceipt is not null
                && DenialEquals(current.AbandonedReceipt, req))
                return current;
        }

        return new AgentToolInvocationPreDispatchResult
        {
            State = r is not null ? AgentToolInvocationPreDispatchState.Abandoned : AgentToolInvocationPreDispatchState.Unknown,
            AbandonedReceipt = r is not null ? abandonedReceipt : null,
            ReasonCode = r is not null ? req.ReasonCode : "pre_dispatch_denial_conflict"
        };
    }

    private async ValueTask<bool> TryMarkDispatchStartedCoreAsync(
        AgentToolInvocationLease lease, AgentToolGovernancePreDispatchReceipt receipt, string reservationId, CancellationToken ct)
    {
        var receiptJson = PostgreSqlRuntimeStoreSupport.Serialize(
            receipt,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolGovernancePreDispatchReceipt);
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                dispatch_started_at = clock_timestamp(),
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
              and expires_at > @now
              and pre_dispatch_state = @ps
              and bound_reservation_id = @rid
              and accepted_receipt_json = @receiptJson
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(new NpgsqlParameter("now", DateTimeOffset.UtcNow));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.DispatchStarted));
        cmd.Parameters.Add(IntParam("ps", (int)AgentToolInvocationPreDispatchState.Accepted));
        cmd.Parameters.Add(new NpgsqlParameter("rid", reservationId));
        cmd.Parameters.Add(JsonParam("receiptJson", receiptJson));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return r is not null;
    }

    private async ValueTask PrepareCompletionCoreAsync(
        AgentToolInvocationLease lease, AgentToolInvocationPrepareCompletionRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(req.Outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(req.BudgetReservationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(req.ReasonCode);
        if (req.Outcome.Kind is not (
                AgentToolInvocationOutcomeKind.Succeeded
                or AgentToolInvocationOutcomeKind.CapabilityFailure
                or AgentToolInvocationOutcomeKind.InternalContractFailure))
            throw new ArgumentException("The completion outcome is not publishable.", nameof(req));

        var requestJson = PostgreSqlRuntimeStoreSupport.Serialize(
            req, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationPrepareCompletionRequest);

        var row = await ReadGateRowAsync(lease, ct).ConfigureAwait(false);
        if (row.State == AgentToolInvocationPreDispatchState.Unknown)
            throw new InvalidOperationException("The invocation lease is stale or unknown.");

        // Same complete request → idempotent; changed request → conflict.
        if (row.State is AgentToolInvocationPreDispatchState.Completed
            or AgentToolInvocationPreDispatchState.CompletionPending)
        {
            if (PostgreSqlRuntimeStoreSupport.JsonEquals(row.CompletionOutcomeJson, requestJson))
                return;
            throw new InvalidOperationException(
                row.State == AgentToolInvocationPreDispatchState.Completed
                    ? "The completed invocation outcome cannot be changed."
                    : "The pending completion outcome cannot be changed.");
        }

        if (row.State != AgentToolInvocationPreDispatchState.DispatchStarted)
            throw new InvalidOperationException("Dispatch has not started.");

        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                completion_outcome_json = @coj,
                completion_prepared_at = @cpa,
                last_reason_code = @rc,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
              and expires_at > @now
              and pre_dispatch_state = @ps
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(new NpgsqlParameter("now", DateTimeOffset.UtcNow));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.CompletionPending));
        cmd.Parameters.Add(IntParam("ps", (int)AgentToolInvocationPreDispatchState.DispatchStarted));
        cmd.Parameters.Add(JsonParam("coj", requestJson));
        cmd.Parameters.Add(new NpgsqlParameter("cpa", DateTimeOffset.UtcNow));
        cmd.Parameters.Add(new NpgsqlParameter("rc", req.ReasonCode));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (r is null)
            throw new InvalidOperationException("The invocation lease is stale or expired.");
    }

    private async ValueTask<AgentToolInvocationCompletionResult> PublishCompletionCoreAsync(
        AgentToolInvocationLease lease, CancellationToken ct)
    {
        var row = await ReadGateRowAsync(lease, ct).ConfigureAwait(false);
        if (row.State == AgentToolInvocationPreDispatchState.Unknown)
            return new AgentToolInvocationCompletionResult { State = AgentToolInvocationCompletionState.Unknown };
        if (row.IsIndeterminate)
            return CompletionResultFromRow(row, AgentToolInvocationCompletionState.Indeterminate);
        if (row.State == AgentToolInvocationPreDispatchState.Completed)
            return CompletionResultFromRow(row, AgentToolInvocationCompletionState.Completed);
        if (row.State != AgentToolInvocationPreDispatchState.CompletionPending)
            return new AgentToolInvocationCompletionResult { State = AgentToolInvocationCompletionState.Unknown };

        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
              and pre_dispatch_state = @ps
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Completed));
        cmd.Parameters.Add(IntParam("ps", (int)AgentToolInvocationPreDispatchState.CompletionPending));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (r is null)
        {
            // Lost CAS race — re-read and produce the current terminal receipt.
            row = await ReadGateRowAsync(lease, ct).ConfigureAwait(false);
            if (row.State == AgentToolInvocationPreDispatchState.Completed)
                return CompletionResultFromRow(row, AgentToolInvocationCompletionState.Completed);
            return new AgentToolInvocationCompletionResult { State = AgentToolInvocationCompletionState.Unknown };
        }

        return CompletionResultFromRow(row, AgentToolInvocationCompletionState.Completed);
    }

    private async ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateCoreAsync(
        AgentToolInvocationLease lease, CancellationToken ct)
    {
        var row = await ReadGateRowAsync(lease, ct).ConfigureAwait(false);
        if (row.State == AgentToolInvocationPreDispatchState.Unknown)
            return new AgentToolInvocationCompletionResult { State = AgentToolInvocationCompletionState.Unknown };
        if (row.State == AgentToolInvocationPreDispatchState.Completed)
            return CompletionResultFromRow(row, AgentToolInvocationCompletionState.Completed);
        if (row.State == AgentToolInvocationPreDispatchState.CompletionPending)
            return CompletionResultFromRow(row, AgentToolInvocationCompletionState.CompletionPending);
        if (row.IsIndeterminate)
            return CompletionResultFromRow(row, AgentToolInvocationCompletionState.Indeterminate);
        return new AgentToolInvocationCompletionResult { State = AgentToolInvocationCompletionState.Unknown };
    }

    private static AgentToolInvocationCompletionResult CompletionResultFromRow(
        GateRow row, AgentToolInvocationCompletionState state)
    {
        AgentToolInvocationPrepareCompletionRequest? request = null;
        if (row.CompletionOutcomeJson is not null)
        {
            request = PostgreSqlRuntimeStoreSupport.Deserialize(
                row.CompletionOutcomeJson,
                PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationPrepareCompletionRequest);
        }

        return new AgentToolInvocationCompletionResult
        {
            State = state,
            Outcome = request?.Outcome,
            PreparedAt = row.CompletionPreparedAt,
            AuditId = request?.AuditId,
            BudgetReservationId = request?.BudgetReservationId,
            ReasonCode = request?.ReasonCode ?? row.LastReasonCode
        };
    }

    private async ValueTask PrepareReleaseCoreAsync(
        AgentToolInvocationLease lease, AgentToolInvocationPrepareReleaseRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentException.ThrowIfNullOrWhiteSpace(req.BudgetReservationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(req.ReasonCode);

        var requestJson = PostgreSqlRuntimeStoreSupport.Serialize(
            req, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationPrepareReleaseRequest);

        var row = await ReadGateRowAsync(lease, ct).ConfigureAwait(false);
        if (row.State == AgentToolInvocationPreDispatchState.Unknown)
            throw new InvalidOperationException("The invocation lease is stale or unknown.");

        // Same complete request → idempotent; changed request → conflict.
        if (row.State is AgentToolInvocationPreDispatchState.Released
            or AgentToolInvocationPreDispatchState.ReleasePending)
        {
            if (PostgreSqlRuntimeStoreSupport.JsonEquals(row.ReleaseOutcomeJson, requestJson))
                return;
            throw new InvalidOperationException(
                row.State == AgentToolInvocationPreDispatchState.Released
                    ? "The released invocation receipt cannot be changed."
                    : "The pending release receipt cannot be changed.");
        }

        if (row.State == AgentToolInvocationPreDispatchState.DispatchStarted)
            throw new InvalidOperationException("A dispatched invocation cannot prepare release.");

        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                release_outcome_json = @roj,
                release_prepared_at = @rpa,
                last_reason_code = @rc,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
              and expires_at > @now
              and pre_dispatch_state = @ps
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(new NpgsqlParameter("now", DateTimeOffset.UtcNow));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.ReleasePending));
        cmd.Parameters.Add(IntParam("ps", (int)AgentToolInvocationPreDispatchState.Accepted));
        cmd.Parameters.Add(JsonParam("roj", requestJson));
        cmd.Parameters.Add(new NpgsqlParameter("rpa", DateTimeOffset.UtcNow));
        cmd.Parameters.Add(new NpgsqlParameter("rc", req.ReasonCode));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (r is null)
            throw new InvalidOperationException("The invocation lease is stale or expired.");
    }

    private async ValueTask<AgentToolInvocationReleaseResult> PublishReleaseCoreAsync(
        AgentToolInvocationLease lease, CancellationToken ct)
    {
        var row = await ReadGateRowAsync(lease, ct).ConfigureAwait(false);
        if (row.State == AgentToolInvocationPreDispatchState.Unknown)
            return new AgentToolInvocationReleaseResult { State = AgentToolInvocationReleaseState.Unknown };
        if (row.IsIndeterminate)
            return ReleaseResultFromRow(row, AgentToolInvocationReleaseState.Indeterminate);
        if (row.State == AgentToolInvocationPreDispatchState.Released)
            return ReleaseResultFromRow(row, AgentToolInvocationReleaseState.Released);
        if (row.State != AgentToolInvocationPreDispatchState.ReleasePending)
            return new AgentToolInvocationReleaseResult { State = AgentToolInvocationReleaseState.Unknown };

        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
              and pre_dispatch_state = @ps
            returning 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Released));
        cmd.Parameters.Add(IntParam("ps", (int)AgentToolInvocationPreDispatchState.ReleasePending));
        var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (r is null)
        {
            // Lost CAS race — re-read and produce the current terminal receipt.
            row = await ReadGateRowAsync(lease, ct).ConfigureAwait(false);
            if (row.State == AgentToolInvocationPreDispatchState.Released)
                return ReleaseResultFromRow(row, AgentToolInvocationReleaseState.Released);
            return new AgentToolInvocationReleaseResult { State = AgentToolInvocationReleaseState.Unknown };
        }

        return ReleaseResultFromRow(row, AgentToolInvocationReleaseState.Released);
    }

    private async ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateCoreAsync(
        AgentToolInvocationLease lease, CancellationToken ct)
    {
        var row = await ReadGateRowAsync(lease, ct).ConfigureAwait(false);
        if (row.State == AgentToolInvocationPreDispatchState.Unknown)
            return new AgentToolInvocationReleaseResult { State = AgentToolInvocationReleaseState.Unknown };
        if (row.State == AgentToolInvocationPreDispatchState.Released)
            return ReleaseResultFromRow(row, AgentToolInvocationReleaseState.Released);
        if (row.State == AgentToolInvocationPreDispatchState.ReleasePending)
            return ReleaseResultFromRow(row, AgentToolInvocationReleaseState.ReleasePending);
        if (row.IsIndeterminate)
            return ReleaseResultFromRow(row, AgentToolInvocationReleaseState.Indeterminate);
        return new AgentToolInvocationReleaseResult { State = AgentToolInvocationReleaseState.Unknown };
    }

    private static AgentToolInvocationReleaseResult ReleaseResultFromRow(
        GateRow row, AgentToolInvocationReleaseState state)
    {
        AgentToolInvocationPrepareReleaseRequest? request = null;
        if (row.ReleaseOutcomeJson is not null)
        {
            request = PostgreSqlRuntimeStoreSupport.Deserialize(
                row.ReleaseOutcomeJson,
                PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationPrepareReleaseRequest);
        }

        return new AgentToolInvocationReleaseResult
        {
            State = state,
            PreparedAt = row.ReleasePreparedAt,
            AuditId = request?.AuditId,
            BudgetReservationId = request?.BudgetReservationId,
            ReasonCode = request?.ReasonCode ?? row.LastReasonCode
        };
    }

    private async ValueTask MarkIndeterminateCoreAsync(AgentToolInvocationLease lease, string reasonCode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        var row = await ReadGateRowAsync(lease, ct).ConfigureAwait(false);
        if (row.State == AgentToolInvocationPreDispatchState.Unknown)
            throw new InvalidOperationException("The invocation lease is stale or unknown.");
        if (row.State == AgentToolInvocationPreDispatchState.Completed
            || row.State == AgentToolInvocationPreDispatchState.Released)
        {
            throw new InvalidOperationException("A published terminal receipt cannot be changed.");
        }
        if (row.IsIndeterminate)
            return;

        // Indeterminate is a logical/operational marker; the underlying
        // Pending/Ready/Accepted recovery substate is preserved.
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set indeterminate_at = @iat,
                indeterminate_reason = @irc,
                last_reason_code = @rc,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where lease_id = @lid
              and attempt_id = @aid
              and fencing_token = @ft
            """;
        cmd.Parameters.Add(new NpgsqlParameter("lid", lease.LeaseId));
        cmd.Parameters.Add(new NpgsqlParameter("aid", lease.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("ft", lease.FencingToken));
        cmd.Parameters.Add(new NpgsqlParameter("iat", DateTimeOffset.UtcNow));
        cmd.Parameters.Add(new NpgsqlParameter("irc", reasonCode));
        cmd.Parameters.Add(new NpgsqlParameter("rc", reasonCode));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> ReleaseByIdentityCoreAsync(
        AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken ct)
    {
        var logicalKeyJson = PostgreSqlRuntimeStoreSupport.Serialize(
            identity.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey);

        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                last_reason_code = @rc,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tid
              and logical_invocation_key = @lk
              and attempt_id = @aid
              and pre_dispatch_state in (@ps_accepted, @ps_ready, @ps_pending)
            returning pre_dispatch_state
            """;
        cmd.Parameters.Add(JsonParam("lk", logicalKeyJson));
        cmd.Parameters.Add(new NpgsqlParameter("tid", identity.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("aid", identity.AttemptId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Released));
        cmd.Parameters.Add(new NpgsqlParameter("rc", reasonCode));
        cmd.Parameters.Add(IntParam("ps_accepted", (int)AgentToolInvocationPreDispatchState.Accepted));
        cmd.Parameters.Add(IntParam("ps_ready", (int)AgentToolInvocationPreDispatchState.Ready));
        cmd.Parameters.Add(IntParam("ps_pending", (int)AgentToolInvocationPreDispatchState.Pending));

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null)
        {
            var current = await GetPreDispatchStateCoreAsync(identity, ct).ConfigureAwait(false);
            if (current.State == AgentToolInvocationPreDispatchState.Released
                && string.Equals(current.ReasonCode, reasonCode, StringComparison.Ordinal))
                return current;

            return new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Unknown,
                ReasonCode = "identity_not_found_or_release_conflict"
            };
        }

        return new AgentToolInvocationPreDispatchResult
        {
            State = AgentToolInvocationPreDispatchState.Released,
            ReasonCode = reasonCode
        };
    }

    private async ValueTask<AgentToolInvocationPreDispatchResult> AbandonByIdentityCoreAsync(
        AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken ct)
    {
        var logicalKeyJson = PostgreSqlRuntimeStoreSupport.Serialize(
            identity.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey);
        var abandonedReceipt = new AgentToolInvocationAbandonedReceipt
        {
            Identity = identity,
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.GovernanceDenied,
                Code = reasonCode,
                Message = reasonCode
            },
            ReasonCode = reasonCode,
            AbandonedAt = DateTimeOffset.UtcNow
        };
        var abandonedReceiptJson = PostgreSqlRuntimeStoreSupport.Serialize(
            abandonedReceipt, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolInvocationAbandonedReceipt);

        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_invocation_pre_dispatch
            set pre_dispatch_state = @st,
                abandoned_receipt_json = @arj,
                last_reason_code = @rc,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tid
              and logical_invocation_key = @lk
              and attempt_id = @aid
              and pre_dispatch_state in (@ps_accepted, @ps_ready, @ps_pending)
            returning pre_dispatch_state
            """;
        cmd.Parameters.Add(JsonParam("lk", logicalKeyJson));
        cmd.Parameters.Add(new NpgsqlParameter("tid", identity.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("aid", identity.AttemptId));
        cmd.Parameters.Add(IntParam("st", (int)AgentToolInvocationPreDispatchState.Abandoned));
        cmd.Parameters.Add(new NpgsqlParameter("rc", reasonCode));
        cmd.Parameters.Add(JsonParam("arj", abandonedReceiptJson));
        cmd.Parameters.Add(IntParam("ps_accepted", (int)AgentToolInvocationPreDispatchState.Accepted));
        cmd.Parameters.Add(IntParam("ps_ready", (int)AgentToolInvocationPreDispatchState.Ready));
        cmd.Parameters.Add(IntParam("ps_pending", (int)AgentToolInvocationPreDispatchState.Pending));

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null)
        {
            var current = await GetPreDispatchStateCoreAsync(identity, ct).ConfigureAwait(false);
            if (current.State == AgentToolInvocationPreDispatchState.Abandoned
                && string.Equals(current.ReasonCode, reasonCode, StringComparison.Ordinal))
                return current;

            return new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Unknown,
                ReasonCode = "identity_not_found_or_abandon_conflict"
            };
        }

        return new AgentToolInvocationPreDispatchResult
        {
            State = AgentToolInvocationPreDispatchState.Abandoned,
            ReasonCode = reasonCode,
            AbandonedReceipt = abandonedReceipt
        };
    }

    private static bool DenialEquals(
        AgentToolInvocationAbandonedReceipt receipt,
        AgentToolInvocationPublishDenialRequest request)
        => string.Equals(receipt.ReasonCode, request.ReasonCode, StringComparison.Ordinal)
            && receipt.Outcome.Kind == request.Outcome.Kind
            && string.Equals(receipt.Outcome.Code, request.Outcome.Code, StringComparison.Ordinal)
            && string.Equals(receipt.Outcome.Message, request.Outcome.Message, StringComparison.Ordinal)
            && JsonEquals(receipt.Outcome.StructuredOutput, request.Outcome.StructuredOutput)
            && receipt.Outcome.Issues.SequenceEqual(request.Outcome.Issues);

    private static bool JsonEquals(JsonElement? left, JsonElement? right)
        => left.HasValue == right.HasValue
            && (!left.HasValue
                || string.Equals(
                    left.Value.GetRawText(),
                    right!.Value.GetRawText(),
                    StringComparison.Ordinal));

    private sealed record GateRow(
        AgentToolInvocationPreDispatchState State,
        string? BoundReservationId = null,
        AgentToolGovernancePreDispatchReceipt? AcceptedReceipt = null,
        AgentToolInvocationAbandonedReceipt? AbandonedReceipt = null,
        AgentToolInvocationPreDispatchIntentSnapshot? Intent = null,
        string? LastReasonCode = null,
        DateTimeOffset? IndeterminateAt = null,
        string? IndeterminateReason = null,
        string? CompletionOutcomeJson = null,
        DateTimeOffset? CompletionPreparedAt = null,
        string? ReleaseOutcomeJson = null,
        DateTimeOffset? ReleasePreparedAt = null)
    {
        public bool IsIndeterminate => IndeterminateAt is not null;
    }
}
