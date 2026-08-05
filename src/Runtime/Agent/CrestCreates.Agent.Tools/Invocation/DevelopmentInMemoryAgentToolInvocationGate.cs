using System.Text.Json;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Volatile single-process invocation gate for development and tests. State is
/// lost on restart and is not coordinated across nodes; terminal receipts are
/// retained for the process lifetime. It provides no durable or distributed
/// exactly-once guarantee.
/// </summary>
public sealed class DevelopmentInMemoryAgentToolInvocationGate
    : IAgentToolInvocationGate, IAgentToolInvocationLeaseAbandoner, IAgentToolPreDispatchPersistenceCapabilities
{
    public AgentToolPreDispatchPersistenceCapability Capability => AgentToolPreDispatchPersistenceCapability.FullSemantic;

    private readonly object _sync = new();
    private readonly Dictionary<AgentToolLogicalInvocationKey, Entry> _entries = [];
    private readonly Dictionary<string, AgentToolLogicalInvocationKey> _leaseKeys
        = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReleaseReceipt> _releaseReceipts
        = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AbandonedReceipt> _abandonedReasons
        = new(StringComparer.Ordinal);
    private readonly Dictionary<AgentToolPreDispatchIdentity, AgentToolInvocationPreDispatchResult> _preDispatchHistory = [];
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
                entry = new Entry(request.InvocationFingerprint) { FingerprintKey = request.Key };
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
            if (entry.LastAttemptState == AttemptTerminalState.ReleasePending)
                return ValueTask.FromResult(Result(AgentToolInvocationAcquireStatus.InProgress));
            if (entry.Indeterminate)
                return ValueTask.FromResult(Result(AgentToolInvocationAcquireStatus.Indeterminate));

            var now = _timeProvider.GetUtcNow();
            if (entry.ActiveLease is { } active)
            {
                if (entry.PreDispatchState == AgentToolInvocationPreDispatchState.Abandoned)
                {
                    ArchivePreDispatch(entry, active);
                    _leaseKeys.Remove(active.LeaseId);
                    entry.ActiveLease = null;
                    entry.LastTerminalLease = active;
                    entry.LastAttemptState = AttemptTerminalState.Abandoned;
                }
                else if (active.ExpiresAt > now)
                    return ValueTask.FromResult(Result(AgentToolInvocationAcquireStatus.InProgress));
                else if (entry.PreDispatchState is (
                    AgentToolInvocationPreDispatchState.Pending
                    or AgentToolInvocationPreDispatchState.Ready
                    or AgentToolInvocationPreDispatchState.Accepted))
                {
                    entry.Indeterminate = true;
                    entry.LastReasonCode = "pre_dispatch_lease_expired";
                    ClearLease(entry, active, AttemptTerminalState.Indeterminate);
                    return ValueTask.FromResult(Result(AgentToolInvocationAcquireStatus.Indeterminate));
                }
                else if (entry.DispatchStarted)
                {
                    entry.Indeterminate = true;
                    entry.LastReasonCode = "post_dispatch_lease_expired";
                    ClearLease(entry, active, AttemptTerminalState.Indeterminate);
                    return ValueTask.FromResult(Result(AgentToolInvocationAcquireStatus.Indeterminate));
                }
                else
                {
                    _leaseKeys.Remove(active.LeaseId);
                    entry.ActiveLease = null;
                }
            }

            if (entry.LastTerminalLease is { } terminalLease)
            {
                if (entry.LastAttemptState is AttemptTerminalState.Abandoned or AttemptTerminalState.Released)
                    ArchivePreDispatch(entry, terminalLease);
                _leaseKeys.Remove(terminalLease.LeaseId);
                ResetForNewAttempt(entry);
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
        AgentToolGovernancePreDispatchReceipt receipt,
        string reservationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!TryGetCurrent(lease, out var entry, out var current)
                || current.ExpiresAt <= _timeProvider.GetUtcNow())
                return ValueTask.FromResult(false);
            if (entry.DispatchStarted)
                return ValueTask.FromResult(true);
            if (entry.PreDispatchState != AgentToolInvocationPreDispatchState.Accepted
                || entry.AcceptedReceipt is null
                || !string.Equals(entry.AcceptedReceipt.AuditId, receipt.AuditId, StringComparison.Ordinal)
                || entry.AcceptedReceipt.AcceptedAt != receipt.AcceptedAt
                || !string.Equals(entry.AcceptedReceipt.Identity.AttemptId, receipt.Identity.AttemptId, StringComparison.Ordinal))
                return ValueTask.FromResult(false);
            if (!string.Equals(entry.BoundReservationId, reservationId, StringComparison.Ordinal))
                return ValueTask.FromResult(false);
            entry.DispatchStarted = true;
            entry.PreDispatchState = AgentToolInvocationPreDispatchState.DispatchStarted;
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

    public ValueTask PrepareReleaseAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPrepareReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BudgetReservationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReasonCode);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (IsSameTerminalTransition(lease, AttemptTerminalState.ReleasePending, out var pending))
            {
                EnsureReleaseRequest(pending, request);
                return ValueTask.CompletedTask;
            }

            if (IsSameTerminalTransition(lease, AttemptTerminalState.Released, out var released))
            {
                EnsureReleaseRequest(released, request);
                return ValueTask.CompletedTask;
            }

            var (entry, _) = GetCurrent(lease);
            if (entry.DispatchStarted)
                throw new InvalidOperationException("A dispatched invocation cannot prepare release.");
            entry.ReleasePendingPreparedAt = _timeProvider.GetUtcNow();
            entry.ReleasePendingAuditId = request.AuditId;
            entry.ReleasePendingBudgetReservationId = request.BudgetReservationId;
            entry.ReleasePendingReasonCode = request.ReasonCode;
            entry.LastReasonCode = request.ReasonCode;
            ClearLease(entry, lease, AttemptTerminalState.ReleasePending);
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<AgentToolInvocationReleaseResult> PublishReleaseAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_releaseReceipts.TryGetValue(lease.LeaseId, out var receipt))
            {
                EnsureLeaseIdentity(receipt.Lease, lease);
                return ValueTask.FromResult(receipt.Result);
            }
            if (IsSameTerminalTransition(lease, AttemptTerminalState.Released, out var released))
                return ValueTask.FromResult(GetReleaseResult(released, AgentToolInvocationReleaseState.Released));
            if (!IsSameTerminalTransition(lease, AttemptTerminalState.ReleasePending, out var pending))
                throw new InvalidOperationException("The invocation has no pending release.");
            pending.LastAttemptState = AttemptTerminalState.Released;
            var result = GetReleaseResult(pending, AgentToolInvocationReleaseState.Released);
            _releaseReceipts[lease.LeaseId] = new ReleaseReceipt(lease, result);
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateAsync(
        AgentToolInvocationLease lease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (IsSameTerminalTransition(lease, AttemptTerminalState.ReleasePending, out var pending))
                return ValueTask.FromResult(GetReleaseResult(pending, AgentToolInvocationReleaseState.ReleasePending));
            if (IsSameTerminalTransition(lease, AttemptTerminalState.Released, out var released))
                return ValueTask.FromResult(GetReleaseResult(released, AgentToolInvocationReleaseState.Released));
            if (IsSameTerminalTransition(lease, AttemptTerminalState.Indeterminate, out var indeterminate))
                return ValueTask.FromResult(GetReleaseResult(indeterminate, AgentToolInvocationReleaseState.Indeterminate));
            if (_releaseReceipts.TryGetValue(lease.LeaseId, out var receipt))
            {
                EnsureLeaseIdentity(receipt.Lease, lease);
                return ValueTask.FromResult(receipt.Result);
            }
            return ValueTask.FromResult(new AgentToolInvocationReleaseResult
            {
                State = AgentToolInvocationReleaseState.Unknown
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

            if (IsSameTerminalTransition(lease, AttemptTerminalState.Released, out _))
            {
                throw new InvalidOperationException("A published release cannot be changed.");
            }

            if (IsSameTerminalTransition(lease, AttemptTerminalState.ReleasePending, out var releasedEntry))
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

    public ValueTask<AgentToolInvocationPreDispatchResult> ReleaseByIdentityAsync(
        AgentToolPreDispatchIdentity identity,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_entries.TryGetValue(identity.LogicalInvocationKey, out var entry))
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = "identity_not_found"
                });

            var attemptLease = entry.ActiveLease ?? entry.LastTerminalLease;
            if (attemptLease is null
                || attemptLease.AttemptId != identity.AttemptId)
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = "attempt_mismatch"
                });

            if (entry.LastAttemptState is AttemptTerminalState.Released)
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = string.Equals(entry.LastReasonCode, reasonCode, StringComparison.Ordinal)
                        ? AgentToolInvocationPreDispatchState.Released
                        : AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = string.Equals(entry.LastReasonCode, reasonCode, StringComparison.Ordinal)
                        ? entry.LastReasonCode
                        : "release_conflict"
                });

            if (entry.LastAttemptState is AttemptTerminalState.Completed)
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Completed,
                    ReasonCode = "already_completed"
                });

            entry.PreDispatchState = AgentToolInvocationPreDispatchState.Released;
            entry.LastReasonCode = reasonCode;
            entry.LastAttemptState = AttemptTerminalState.Released;
            ClearLease(entry, attemptLease, AttemptTerminalState.Released);
            ArchivePreDispatch(entry, attemptLease);
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Released,
                ReasonCode = reasonCode
            });
        }
    }

    public ValueTask<AgentToolInvocationPreDispatchResult> AbandonByIdentityAsync(
        AgentToolPreDispatchIdentity identity,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_entries.TryGetValue(identity.LogicalInvocationKey, out var entry))
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = "identity_not_found"
                });

            var attemptLease = entry.ActiveLease ?? entry.LastTerminalLease;
            if (attemptLease is null
                || attemptLease.AttemptId != identity.AttemptId)
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = "attempt_mismatch"
                });

            if (entry.LastAttemptState is AttemptTerminalState.Released
                or AttemptTerminalState.Completed)
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = entry.LastAttemptState == AttemptTerminalState.Released
                        ? AgentToolInvocationPreDispatchState.Released
                        : AgentToolInvocationPreDispatchState.Completed,
                    ReasonCode = "already_terminal"
                });

            if (entry.LastAttemptState is AttemptTerminalState.Abandoned)
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = string.Equals(entry.LastReasonCode, reasonCode, StringComparison.Ordinal)
                        ? AgentToolInvocationPreDispatchState.Abandoned
                        : AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = string.Equals(entry.LastReasonCode, reasonCode, StringComparison.Ordinal)
                        ? entry.LastReasonCode
                        : "abandon_conflict"
                });

            entry.PreDispatchState = AgentToolInvocationPreDispatchState.Abandoned;
            entry.LastReasonCode = reasonCode;
            entry.LastAttemptState = AttemptTerminalState.Abandoned;
            ClearLease(entry, attemptLease, AttemptTerminalState.Abandoned);
            ArchivePreDispatch(entry, attemptLease);
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Abandoned,
                ReasonCode = reasonCode
            });
        }
    }

    public ValueTask<AgentToolInvocationPreDispatchResult> PreparePreDispatchIntentAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPreparePreDispatchIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Intent);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var (entry, _) = GetCurrent(lease);
            if (entry.PreDispatchState == AgentToolInvocationPreDispatchState.Pending)
            {
                return ValueTask.FromResult(
                    entry.Intent is not null
                    && AgentToolGovernancePreDispatchComparer.Equivalent(
                        entry.Intent,
                        request.Intent)
                        ? new AgentToolInvocationPreDispatchResult
                        {
                            State = AgentToolInvocationPreDispatchState.Pending,
                            Intent = entry.Intent
                        }
                        : new AgentToolInvocationPreDispatchResult
                        {
                            State = AgentToolInvocationPreDispatchState.Unknown,
                            ReasonCode = "pre_dispatch_intent_conflict"
                        });
            }

            if (entry.PreDispatchState != AgentToolInvocationPreDispatchState.Unknown)
            {
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = entry.PreDispatchState,
                    ReasonCode = "pre_dispatch_intent_already_prepared"
                });
            }

            entry.Intent = request.Intent;
            entry.PreDispatchState = AgentToolInvocationPreDispatchState.Pending;
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Pending,
                Intent = entry.Intent
            });
        }
    }

    public ValueTask<AgentToolInvocationPreDispatchResult> BindPreDispatchReservationAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Reservation);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReservationId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var (entry, _) = GetCurrent(lease);
            if (entry.PreDispatchState == AgentToolInvocationPreDispatchState.Ready)
            {
                return ValueTask.FromResult(
                    entry.BoundReservation is not null
                    && string.Equals(
                        entry.BoundReservationId,
                        request.ReservationId,
                        StringComparison.Ordinal)
                    && AgentToolGovernancePreDispatchComparer.ReservationIdentityAndTermsEqual(
                        entry.BoundReservation,
                        request.Reservation)
                    && entry.BoundReservation.State == request.Reservation.State
                        ? new AgentToolInvocationPreDispatchResult
                        {
                            State = AgentToolInvocationPreDispatchState.Ready,
                            Intent = entry.Intent,
                            BoundReservationId = entry.BoundReservationId
                        }
                        : new AgentToolInvocationPreDispatchResult
                        {
                            State = AgentToolInvocationPreDispatchState.Unknown,
                            ReasonCode = "pre_dispatch_reservation_conflict"
                        });
            }

            if (entry.PreDispatchState != AgentToolInvocationPreDispatchState.Pending)
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = entry.PreDispatchState,
                    ReasonCode = "pre_dispatch_not_pending"
                });

            if (!string.Equals(
                    request.ReservationId,
                    request.Reservation.ReservationId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    request.Reservation.AttemptId,
                    lease.AttemptId,
                    StringComparison.Ordinal)
                || entry.Intent is null
                || !string.Equals(
                    request.Reservation.InvocationFingerprint,
                    entry.Intent.InvocationFingerprint,
                    StringComparison.Ordinal))
            {
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = "pre_dispatch_reservation_conflict"
                });
            }

            entry.BoundReservationId = request.ReservationId;
            entry.BoundReservation = request.Reservation;
            entry.PreDispatchState = AgentToolInvocationPreDispatchState.Ready;
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Ready,
                Intent = entry.Intent,
                BoundReservationId = entry.BoundReservationId
            });
        }
    }

    public ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedPreDispatchAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationBindPreDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Receipt);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var (entry, _) = GetCurrent(lease);
            if (entry.PreDispatchState == AgentToolInvocationPreDispatchState.Accepted)
            {
                return ValueTask.FromResult(
                    entry.AcceptedReceipt is not null
                    && AgentToolGovernancePreDispatchComparer.Equivalent(
                        entry.AcceptedReceipt,
                        request.Receipt)
                        ? new AgentToolInvocationPreDispatchResult
                        {
                            State = AgentToolInvocationPreDispatchState.Accepted,
                            Intent = entry.Intent,
                            BoundReservationId = entry.BoundReservationId,
                            AcceptedReceipt = entry.AcceptedReceipt
                        }
                        : new AgentToolInvocationPreDispatchResult
                        {
                            State = AgentToolInvocationPreDispatchState.Unknown,
                            ReasonCode = "pre_dispatch_receipt_conflict"
                        });
            }

            if (entry.PreDispatchState != AgentToolInvocationPreDispatchState.Ready)
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = entry.PreDispatchState,
                    ReasonCode = "pre_dispatch_not_ready"
                });

            var expectedIdentity = new AgentToolPreDispatchIdentity(
                entry.FingerprintKey,
                lease.AttemptId);
            if (request.Receipt.Identity != expectedIdentity)
            {
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = "pre_dispatch_receipt_conflict"
                });
            }

            entry.AcceptedReceipt = request.Receipt;
            entry.PreDispatchState = AgentToolInvocationPreDispatchState.Accepted;
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Accepted,
                Intent = entry.Intent,
                BoundReservationId = entry.BoundReservationId,
                AcceptedReceipt = entry.AcceptedReceipt
            });
        }
    }

    public ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_preDispatchHistory.TryGetValue(identity, out var historical))
                return ValueTask.FromResult(historical);

            if (!_entries.TryGetValue(identity.LogicalInvocationKey, out var entry))
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown
                });

            var attemptLease = entry.ActiveLease ?? entry.LastTerminalLease;
            if (attemptLease is null
                || attemptLease.AttemptId != identity.AttemptId)
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown
                });

            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = entry.PreDispatchState,
                Indeterminate = entry.Indeterminate,
                Intent = entry.Intent,
                BoundReservationId = entry.BoundReservationId,
                AcceptedReceipt = entry.AcceptedReceipt,
                AbandonedReceipt = entry.AbandonedReceipt,
                ReasonCode = entry.LastReasonCode
            });
        }
    }

    public ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialAsync(
        AgentToolInvocationLease lease,
        AgentToolInvocationPublishDenialRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReasonCode);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            AgentToolInvocationPreDispatchResult? historical = null;
            foreach (var pair in _preDispatchHistory)
            {
                if (string.Equals(pair.Key.AttemptId, lease.AttemptId, StringComparison.Ordinal))
                {
                    historical = pair.Value;
                    break;
                }
            }

            if (historical is not null)
            {
                return ValueTask.FromResult(
                    historical.AbandonedReceipt is not null
                    && string.Equals(historical.AbandonedReceipt.ReasonCode, request.ReasonCode, StringComparison.Ordinal)
                    && Equivalent(historical.AbandonedReceipt.Outcome, request.Outcome)
                        ? historical
                        : new AgentToolInvocationPreDispatchResult
                        {
                            State = AgentToolInvocationPreDispatchState.Unknown,
                            ReasonCode = "pre_dispatch_denial_conflict"
                        });
            }

            var (entry, _) = GetCurrent(lease);
            if (entry.PreDispatchState is not (
                AgentToolInvocationPreDispatchState.Pending
                or AgentToolInvocationPreDispatchState.Abandoned))
            {
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = entry.PreDispatchState,
                    ReasonCode = "pre_dispatch_not_pending"
                });
            }

            if (entry.AbandonedReceipt is not null)
            {
                if (!string.Equals(
                        entry.AbandonedReceipt.ReasonCode,
                        request.ReasonCode,
                        StringComparison.Ordinal)
                    || !Equivalent(entry.AbandonedReceipt.Outcome, request.Outcome))
                    return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                    {
                        State = entry.PreDispatchState,
                        ReasonCode = "pre_dispatch_denial_conflict"
                    });
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Abandoned,
                    AbandonedReceipt = entry.AbandonedReceipt
                });
            }

            var identity = new AgentToolPreDispatchIdentity(
                entry.FingerprintKey,
                lease.AttemptId);
            var abandoned = new AgentToolInvocationAbandonedReceipt
            {
                Identity = identity,
                Outcome = request.Outcome,
                ReasonCode = request.ReasonCode,
                AbandonedAt = _timeProvider.GetUtcNow()
            };
            entry.AbandonedReceipt = abandoned;
            entry.PreDispatchState = AgentToolInvocationPreDispatchState.Abandoned;
            entry.LastReasonCode = request.ReasonCode;
            ArchivePreDispatch(entry, lease);
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Abandoned,
                AbandonedReceipt = abandoned
            });
        }
    }

    public ValueTask AbandonUnrecordedLeaseAsync(
        AgentToolInvocationLease lease,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_releaseReceipts.ContainsKey(lease.LeaseId))
                throw new InvalidOperationException("A published release cannot be abandoned.");

            if (_abandonedReasons.TryGetValue(lease.LeaseId, out var abandonedReceipt))
            {
                EnsureLeaseIdentity(abandonedReceipt.Lease, lease);
                if (!string.Equals(abandonedReceipt.ReasonCode, reasonCode, StringComparison.Ordinal))
                    throw new InvalidOperationException("The abandoned lease reason cannot be changed.");
                return ValueTask.CompletedTask;
            }

            if (IsSameTerminalTransition(lease, AttemptTerminalState.Released, out _))
                throw new InvalidOperationException("A published release cannot be abandoned.");

            var (entry, _) = GetCurrent(lease);
            if (entry.DispatchStarted)
                throw new InvalidOperationException("A dispatched invocation lease cannot be released.");
            entry.LastReasonCode = reasonCode;
            _abandonedReasons[lease.LeaseId] = new AbandonedReceipt(lease, reasonCode);
            ClearLease(entry, lease, AttemptTerminalState.Abandoned);
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

    private void ArchivePreDispatch(Entry entry, AgentToolInvocationLease lease)
    {
        var identity = new AgentToolPreDispatchIdentity(entry.FingerprintKey, lease.AttemptId);
        _preDispatchHistory[identity] = new AgentToolInvocationPreDispatchResult
        {
            State = entry.PreDispatchState,
            Intent = entry.Intent,
            BoundReservationId = entry.BoundReservationId,
            AcceptedReceipt = entry.AcceptedReceipt,
            AbandonedReceipt = entry.AbandonedReceipt,
            ReasonCode = entry.LastReasonCode
        };
    }

    private static void ResetForNewAttempt(Entry entry)
    {
        entry.ActiveLease = null;
        entry.LastTerminalLease = null;
        entry.LastAttemptState = AttemptTerminalState.None;
        entry.PreDispatchState = AgentToolInvocationPreDispatchState.Unknown;
        entry.Intent = null;
        entry.BoundReservationId = null;
        entry.BoundReservation = null;
        entry.AcceptedReceipt = null;
        entry.AbandonedReceipt = null;
        entry.DispatchStarted = false;
        entry.Indeterminate = false;
        entry.LastReasonCode = null;
        entry.ReleasePendingPreparedAt = null;
        entry.ReleasePendingAuditId = null;
        entry.ReleasePendingBudgetReservationId = null;
        entry.ReleasePendingReasonCode = null;
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

    private static void EnsureLeaseIdentity(
        AgentToolInvocationLease expected,
        AgentToolInvocationLease actual)
    {
        if (!SameLeaseIdentity(expected, actual))
            throw new InvalidOperationException("The invocation lease fencing identity does not match.");
    }

    private static bool SameLeaseIdentity(
        AgentToolInvocationLease left,
        AgentToolInvocationLease right)
        => string.Equals(left.LeaseId, right.LeaseId, StringComparison.Ordinal)
            && string.Equals(left.AttemptId, right.AttemptId, StringComparison.Ordinal)
            && left.FencingToken == right.FencingToken;

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

    private static AgentToolInvocationReleaseResult GetReleaseResult(
        Entry entry,
        AgentToolInvocationReleaseState state)
        => new()
        {
            State = state,
            PreparedAt = entry.ReleasePendingPreparedAt,
            AuditId = entry.ReleasePendingAuditId,
            BudgetReservationId = entry.ReleasePendingBudgetReservationId,
            ReasonCode = entry.ReleasePendingReasonCode ?? entry.LastReasonCode
        };

    private static void EnsureReleaseRequest(
        Entry entry,
        AgentToolInvocationPrepareReleaseRequest request)
    {
        if (!string.Equals(entry.ReleasePendingAuditId, request.AuditId, StringComparison.Ordinal)
            || !string.Equals(
                entry.ReleasePendingBudgetReservationId,
                request.BudgetReservationId,
                StringComparison.Ordinal)
            || !string.Equals(
                entry.ReleasePendingReasonCode ?? entry.LastReasonCode,
                request.ReasonCode,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The pending release identity cannot be changed.");
        }
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
        public AgentToolLogicalInvocationKey FingerprintKey { get; set; }
        public long FencingToken { get; set; }
        public AgentToolInvocationLease? ActiveLease { get; set; }
        public bool DispatchStarted { get; set; }
        public bool Indeterminate { get; set; }
        public AgentToolInvocationPreDispatchState PreDispatchState { get; set; }
            = AgentToolInvocationPreDispatchState.Unknown;
        public AgentToolInvocationPreDispatchIntentSnapshot? Intent { get; set; }
        public string? BoundReservationId { get; set; }
        public AgentToolBudgetReservation? BoundReservation { get; set; }
        public AgentToolGovernancePreDispatchReceipt? AcceptedReceipt { get; set; }
        public AgentToolInvocationAbandonedReceipt? AbandonedReceipt { get; set; }
        public AgentToolInvocationOutcome? CompletedOutcome { get; set; }
        public AgentToolInvocationOutcome? CompletionPendingOutcome { get; set; }
        public DateTimeOffset? CompletionPendingPreparedAt { get; set; }
        public string? CompletionPendingAuditId { get; set; }
        public string? CompletionPendingBudgetReservationId { get; set; }
        public string? CompletionPendingReasonCode { get; set; }
        public DateTimeOffset? ReleasePendingPreparedAt { get; set; }
        public string? ReleasePendingAuditId { get; set; }
        public string? ReleasePendingBudgetReservationId { get; set; }
        public string? ReleasePendingReasonCode { get; set; }
        public string? LastReasonCode { get; set; }
        public AgentToolInvocationLease? LastTerminalLease { get; set; }
        public AttemptTerminalState LastAttemptState { get; set; }
    }

    private sealed record ReleaseReceipt(
        AgentToolInvocationLease Lease,
        AgentToolInvocationReleaseResult Result);

    private sealed record AbandonedReceipt(
        AgentToolInvocationLease Lease,
        string ReasonCode);

    private enum AttemptTerminalState
    {
        None,
        Abandoned,
        ReleasePending,
        Released,
        CompletionPending,
        Completed,
        Indeterminate
    }
}
