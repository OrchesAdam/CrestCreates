using System.Text.Json;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Volatile single-process invocation gate for development and tests. State is
/// lost on restart and is not coordinated across nodes; it provides no durable
/// or distributed exactly-once guarantee.
/// </summary>
public sealed class DevelopmentInMemoryAgentToolInvocationGate
    : IAgentToolInvocationGate
{
    private readonly object _sync = new();
    private readonly Dictionary<AgentToolLogicalInvocationKey, Entry> _entries = [];
    private readonly Dictionary<string, AgentToolLogicalInvocationKey> _leaseKeys
        = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _leaseDuration;

    public DevelopmentInMemoryAgentToolInvocationGate(
        TimeProvider? timeProvider = null,
        TimeSpan? leaseDuration = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _leaseDuration = leaseDuration ?? TimeSpan.FromMinutes(1);
        if (_leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
    }

    public ValueTask<AgentToolInvocationAcquireResult> AcquireAsync(
        AgentToolInvocationAcquireRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AgentToolGovernanceGuard.IsValid(request.Key)
            || string.IsNullOrWhiteSpace(request.InvocationFingerprint))
            throw new ArgumentException("Invocation acquire request is invalid.", nameof(request));

        lock (_sync)
        {
            if (!_entries.TryGetValue(request.Key, out var entry))
            {
                entry = new Entry(request.InvocationFingerprint);
                _entries.Add(request.Key, entry);
            }
            else if (!string.Equals(entry.Fingerprint, request.InvocationFingerprint, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(Result(AgentToolInvocationAcquireStatus.Conflict));
            }

            if (entry.CompletedOutcome is not null)
                return ValueTask.FromResult(Result(AgentToolInvocationAcquireStatus.Completed, outcome: entry.CompletedOutcome));
            if (entry.CompletionPendingOutcome is not null)
                return ValueTask.FromResult(Result(AgentToolInvocationAcquireStatus.InProgress));
            if (entry.Indeterminate)
                return ValueTask.FromResult(Result(AgentToolInvocationAcquireStatus.Indeterminate));

            var now = _timeProvider.GetUtcNow();
            if (entry.ActiveLease is { } active)
            {
                if (active.ExpiresAt > now)
                    return ValueTask.FromResult(Result(AgentToolInvocationAcquireStatus.InProgress));
                if (entry.DispatchStarted)
                {
                    entry.Indeterminate = true;
                    entry.LastReasonCode = "post_dispatch_lease_expired";
                    ClearLease(entry, active, AttemptTerminalState.Indeterminate);
                    return ValueTask.FromResult(Result(AgentToolInvocationAcquireStatus.Indeterminate));
                }
                _leaseKeys.Remove(active.LeaseId);
                entry.ActiveLease = null;
            }

            if (entry.LastTerminalLease is { } terminalLease)
            {
                _leaseKeys.Remove(terminalLease.LeaseId);
                entry.LastTerminalLease = null;
                entry.LastAttemptState = AttemptTerminalState.None;
            }

            entry.FencingToken++;
            var lease = new AgentToolInvocationLease
            {
                AttemptId = $"attempt-{Guid.NewGuid():N}",
                LeaseId = $"lease-{Guid.NewGuid():N}",
                FencingToken = entry.FencingToken,
                AcquiredAt = now,
                ExpiresAt = now.Add(_leaseDuration)
            };
            entry.ActiveLease = lease;
            entry.DispatchStarted = false;
            _leaseKeys.Add(lease.LeaseId, request.Key);
            return ValueTask.FromResult(Result(AgentToolInvocationAcquireStatus.Acquired, lease));
        }
    }

    public ValueTask<AgentToolInvocationLease> RenewAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var (entry, current) = GetCurrent(lease);
            var now = _timeProvider.GetUtcNow();
            if (current.ExpiresAt <= now)
                throw new InvalidOperationException("The invocation lease has expired.");
            var renewed = current with { ExpiresAt = now.Add(_leaseDuration) };
            entry.ActiveLease = renewed;
            return ValueTask.FromResult(renewed);
        }
    }

    public ValueTask<bool> TryMarkDispatchStartedAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!TryGetCurrent(lease, out var entry, out var current)
                || current.ExpiresAt <= _timeProvider.GetUtcNow())
                return ValueTask.FromResult(false);
            if (entry.DispatchStarted)
                return ValueTask.FromResult(true);
            entry.DispatchStarted = true;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask PrepareCompletionAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPrepareCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BudgetReservationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReasonCode);
        ValidateCompletionOutcome(request.Outcome);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (IsSameTerminalTransition(lease, AttemptTerminalState.Completed, out var terminalEntry))
            {
                if (!Equivalent(terminalEntry.CompletedOutcome, request.Outcome)
                    || !MatchesPreparationIdentity(terminalEntry, request))
                    throw new InvalidOperationException("The completed invocation outcome cannot be changed.");
                return ValueTask.CompletedTask;
            }

            if (IsSameTerminalTransition(lease, AttemptTerminalState.CompletionPending, out var pendingEntry))
            {
                if (!Equivalent(pendingEntry.CompletionPendingOutcome, request.Outcome)
                    || !MatchesPreparationIdentity(pendingEntry, request))
                    throw new InvalidOperationException("The pending completion outcome cannot be changed.");
                return ValueTask.CompletedTask;
            }

            var (entry, _) = GetCurrent(lease);
            if (!entry.DispatchStarted)
                throw new InvalidOperationException("Dispatch has not started.");
            entry.CompletionPendingOutcome = request.Outcome;
            entry.CompletionPendingPreparedAt = _timeProvider.GetUtcNow();
            entry.CompletionPendingAuditId = request.AuditId;
            entry.CompletionPendingBudgetReservationId = request.BudgetReservationId;
            entry.CompletionPendingReasonCode = request.ReasonCode;
            entry.LastReasonCode = request.ReasonCode;
            ClearLease(entry, lease, AttemptTerminalState.CompletionPending);
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<AgentToolInvocationCompletionResult> PublishCompletionAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (IsSameTerminalTransition(lease, AttemptTerminalState.Completed, out _))
                return ValueTask.FromResult(GetCompletionResult(lease, AgentToolInvocationCompletionState.Completed));

            if (!IsSameTerminalTransition(lease, AttemptTerminalState.CompletionPending, out var entry)
                || entry.CompletionPendingOutcome is null)
                throw new InvalidOperationException("The invocation has no pending completion.");

            entry.CompletedOutcome = entry.CompletionPendingOutcome;
            entry.CompletionPendingOutcome = null;
            entry.LastAttemptState = AttemptTerminalState.Completed;
            return ValueTask.FromResult(GetCompletionResult(lease, AgentToolInvocationCompletionState.Completed));
        }
    }

    public ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (IsSameTerminalTransition(lease, AttemptTerminalState.Completed, out _))
                return ValueTask.FromResult(GetCompletionResult(lease, AgentToolInvocationCompletionState.Completed));
            if (IsSameTerminalTransition(lease, AttemptTerminalState.CompletionPending, out _))
                return ValueTask.FromResult(GetCompletionResult(lease, AgentToolInvocationCompletionState.CompletionPending));
            if (IsSameTerminalTransition(lease, AttemptTerminalState.Indeterminate, out _))
                return ValueTask.FromResult(GetCompletionResult(lease, AgentToolInvocationCompletionState.Indeterminate));
            return ValueTask.FromResult(new AgentToolInvocationCompletionResult
            {
                State = AgentToolInvocationCompletionState.Unknown
            });
        }
    }

    public ValueTask MarkIndeterminateAsync(
        AgentToolInvocationLease lease,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (IsSameTerminalTransition(lease, AttemptTerminalState.Indeterminate, out _))
                return ValueTask.CompletedTask;

            if (IsSameTerminalTransition(lease, AttemptTerminalState.Completed, out _))
            {
                throw new InvalidOperationException("A published completion cannot be changed.");
            }

            if (IsSameTerminalTransition(lease, AttemptTerminalState.CompletionPending, out var pendingEntry))
            {
                pendingEntry.CompletionPendingOutcome = null;
                pendingEntry.CompletionPendingPreparedAt = null;
                pendingEntry.CompletionPendingAuditId = null;
                pendingEntry.CompletionPendingBudgetReservationId = null;
                pendingEntry.CompletionPendingReasonCode = null;
                pendingEntry.Indeterminate = true;
                pendingEntry.LastReasonCode = reasonCode;
                pendingEntry.LastAttemptState = AttemptTerminalState.Indeterminate;
                return ValueTask.CompletedTask;
            }

            if (IsSameTerminalTransition(lease, AttemptTerminalState.Released, out var releasedEntry))
            {
                releasedEntry.Indeterminate = true;
                releasedEntry.LastReasonCode = reasonCode;
                releasedEntry.LastAttemptState = AttemptTerminalState.Indeterminate;
                return ValueTask.CompletedTask;
            }

            var (entry, _) = GetCurrent(lease);
            entry.Indeterminate = true;
            entry.LastReasonCode = reasonCode;
            ClearLease(entry, lease, AttemptTerminalState.Indeterminate);
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask ReleaseLeaseAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (IsSameTerminalTransition(lease, AttemptTerminalState.Released, out _))
                return ValueTask.CompletedTask;

            var (entry, _) = GetCurrent(lease);
            if (entry.DispatchStarted)
                throw new InvalidOperationException("A dispatched invocation lease cannot be released.");
            ClearLease(entry, lease, AttemptTerminalState.Released);
            return ValueTask.CompletedTask;
        }
    }

    private (Entry Entry, AgentToolInvocationLease Lease) GetCurrent(AgentToolInvocationLease lease)
    {
        if (!TryGetCurrent(lease, out var entry, out var current))
            throw new InvalidOperationException("The invocation lease is stale or unknown.");
        if (current.ExpiresAt <= _timeProvider.GetUtcNow())
            throw new InvalidOperationException("The invocation lease has expired.");
        return (entry, current);
    }

    private bool TryGetCurrent(
        AgentToolInvocationLease lease,
        out Entry entry,
        out AgentToolInvocationLease current)
    {
        entry = null!;
        current = null!;
        if (lease is null
            || !_leaseKeys.TryGetValue(lease.LeaseId, out var key)
            || !_entries.TryGetValue(key, out var found)
            || found.ActiveLease is not { } active
            || active.LeaseId != lease.LeaseId
            || active.FencingToken != lease.FencingToken
            || active.AttemptId != lease.AttemptId)
        {
            return false;
        }

        entry = found;
        current = active;
        return true;
    }

    private bool IsSameTerminalTransition(
        AgentToolInvocationLease lease,
        AttemptTerminalState state,
        out Entry entry)
    {
        entry = null!;
        if (lease is null
            || !_leaseKeys.TryGetValue(lease.LeaseId, out var key)
            || !_entries.TryGetValue(key, out var found)
            || found.LastTerminalLease is not { } terminal
            || terminal.LeaseId != lease.LeaseId
            || terminal.FencingToken != lease.FencingToken
            || terminal.AttemptId != lease.AttemptId
            || found.LastAttemptState != state)
        {
            return false;
        }

        entry = found;
        return true;
    }

    private void ClearLease(
        Entry entry,
        AgentToolInvocationLease lease,
        AttemptTerminalState state)
    {
        entry.ActiveLease = null;
        entry.LastTerminalLease = lease;
        entry.LastAttemptState = state;
    }

    private static AgentToolInvocationAcquireResult Result(
        AgentToolInvocationAcquireStatus status,
        AgentToolInvocationLease? lease = null,
        AgentToolInvocationOutcome? outcome = null)
        => new() { Status = status, Lease = lease, CompletedOutcome = outcome };

    private static void ValidateCompletionOutcome(AgentToolInvocationOutcome outcome)
    {
        if (outcome.Kind is not (
                AgentToolInvocationOutcomeKind.Succeeded
                or AgentToolInvocationOutcomeKind.CapabilityFailure
                or AgentToolInvocationOutcomeKind.InternalContractFailure))
            throw new ArgumentException("The completion outcome is not publishable.", nameof(outcome));
    }

    private static bool MatchesPreparationIdentity(
        Entry entry,
        AgentToolInvocationPrepareCompletionRequest request)
        => string.Equals(entry.CompletionPendingAuditId, request.AuditId, StringComparison.Ordinal)
            && string.Equals(
                entry.CompletionPendingBudgetReservationId,
                request.BudgetReservationId,
                StringComparison.Ordinal)
            && string.Equals(
                entry.CompletionPendingReasonCode,
                request.ReasonCode,
                StringComparison.Ordinal);

    private AgentToolInvocationCompletionResult GetCompletionResult(
        AgentToolInvocationLease lease,
        AgentToolInvocationCompletionState state)
    {
        if (!_leaseKeys.TryGetValue(lease.LeaseId, out var key)
            || !_entries.TryGetValue(key, out var entry))
            return new AgentToolInvocationCompletionResult
            {
                State = AgentToolInvocationCompletionState.Unknown
            };

        return new AgentToolInvocationCompletionResult
        {
            State = state,
            Outcome = state == AgentToolInvocationCompletionState.Completed
                ? entry.CompletedOutcome
                : entry.CompletionPendingOutcome,
            PreparedAt = entry.CompletionPendingPreparedAt,
            AuditId = entry.CompletionPendingAuditId,
            BudgetReservationId = entry.CompletionPendingBudgetReservationId,
            ReasonCode = entry.CompletionPendingReasonCode
                ?? entry.LastReasonCode
        };
    }

    private static bool Equivalent(
        AgentToolInvocationOutcome? left,
        AgentToolInvocationOutcome right)
        => left is not null
            && left.Kind == right.Kind
            && string.Equals(left.Code, right.Code, StringComparison.Ordinal)
            && string.Equals(left.Message, right.Message, StringComparison.Ordinal)
            && JsonEquals(left.StructuredOutput, right.StructuredOutput)
            && left.Issues.SequenceEqual(right.Issues);

    private static bool JsonEquals(JsonElement? left, JsonElement? right)
        => left.HasValue == right.HasValue
            && (!left.HasValue
                || string.Equals(
                    left.Value.GetRawText(),
                    right!.Value.GetRawText(),
                    StringComparison.Ordinal));

    private sealed class Entry(string fingerprint)
    {
        public string Fingerprint { get; } = fingerprint;
        public long FencingToken { get; set; }
        public AgentToolInvocationLease? ActiveLease { get; set; }
        public bool DispatchStarted { get; set; }
        public bool Indeterminate { get; set; }
        public AgentToolInvocationOutcome? CompletedOutcome { get; set; }
        public AgentToolInvocationOutcome? CompletionPendingOutcome { get; set; }
        public DateTimeOffset? CompletionPendingPreparedAt { get; set; }
        public string? CompletionPendingAuditId { get; set; }
        public string? CompletionPendingBudgetReservationId { get; set; }
        public string? CompletionPendingReasonCode { get; set; }
        public string? LastReasonCode { get; set; }
        public AgentToolInvocationLease? LastTerminalLease { get; set; }
        public AttemptTerminalState LastAttemptState { get; set; }
    }

    private enum AttemptTerminalState
    {
        None,
        Released,
        CompletionPending,
        Completed,
        Indeterminate
    }
}
