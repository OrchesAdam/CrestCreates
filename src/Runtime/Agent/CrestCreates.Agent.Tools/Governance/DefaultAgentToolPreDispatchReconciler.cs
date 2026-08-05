using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Agent.Abstractions;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Default runtime-owned reconciler that reads Gate, Budget, and checkpoint
/// in the fixed Spec order (§7.6). Never dispatches, never evaluates approval,
/// never creates budget reservations. The Accountability producer is wired as
/// an optional no-op collaborator until Slice 6.
/// </summary>
public sealed class DefaultAgentToolPreDispatchReconciler : IAgentToolPreDispatchReconciler
{
    private readonly IAgentToolInvocationGate _gate;
    private readonly IAgentToolBudgetGate _budgetGate;
    private readonly IAgentToolGovernanceAuditor _auditor;
    private readonly IAgentToolPreDispatchReconciliationStore _store;
    private readonly IAgentToolPreDispatchReconciliationAccountabilityProducer? _accountabilityProducer;
    private readonly TimeProvider _timeProvider;

    public DefaultAgentToolPreDispatchReconciler(
        IAgentToolInvocationGate gate,
        IAgentToolBudgetGate budgetGate,
        IAgentToolGovernanceAuditor auditor,
        IAgentToolPreDispatchReconciliationStore store,
        TimeProvider? timeProvider = null,
        IAgentToolPreDispatchReconciliationAccountabilityProducer? accountabilityProducer = null)
    {
        _gate = gate;
        _budgetGate = budgetGate;
        _auditor = auditor;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _accountabilityProducer = accountabilityProducer;
    }

    public async ValueTask<AgentToolPreDispatchReconciliationResult> ReconcileAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Read Gate Attempt by exact identity.
        var gateState = await _gate.GetPreDispatchStateAsync(identity, cancellationToken);

        // Step 2: Reject Missing, post-dispatch, or incompatible states.
        // Missing gate state (Unknown) is NOT terminal — the attempt may not have started yet
        // or may have been cleaned up. Return StillPending observation, not a terminal receipt.
        if (gateState.State == AgentToolInvocationPreDispatchState.Unknown)
        {
            return await CreateObservationResultAsync(
                identity, AgentToolPreDispatchReconciliationStatus.StillPending, "gate_missing", cancellationToken);
        }

        if (gateState.State is AgentToolInvocationPreDispatchState.DispatchStarted
            or AgentToolInvocationPreDispatchState.CompletionPending
            or AgentToolInvocationPreDispatchState.Completed)
        {
            return await CreateTerminalResultAsync(identity, AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown, "dispatch_started", cancellationToken);
        }

        if (gateState.State is AgentToolInvocationPreDispatchState.Abandoned
            or AgentToolInvocationPreDispatchState.Released)
        {
            // Already terminal — check for existing receipt.
            var existingReceipt = await _store.ReadReceiptAsync(identity, cancellationToken);
            if (existingReceipt is not null)
            {
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = ReplayStatus(existingReceipt),
                    Receipt = existingReceipt
                };
            }

            // The Gate terminal CAS is authoritative control evidence. A crash
            // may occur after that commit but before receipt insertion, so create
            // the first immutable receipt instead of freezing a safely closed
            // Attempt as Conflict.
            return await CreateTerminalResultAsync(
                identity,
                AgentToolPreDispatchReconciliationStatus.Released,
                gateState.ReasonCode ?? (gateState.State == AgentToolInvocationPreDispatchState.Abandoned
                    ? "abandoned_terminal_recovered"
                    : "released_terminal_recovered"),
                cancellationToken).ConfigureAwait(false);
        }

        // Check for existing terminal receipt before proceeding (idempotent reconciliation).
        var priorReceipt = await _store.ReadReceiptAsync(identity, cancellationToken);
        if (priorReceipt is not null)
        {
            return new AgentToolPreDispatchReconciliationResult
            {
                Status = ReplayStatus(priorReceipt),
                Receipt = priorReceipt
            };
        }

        // Step 3: Read budget reservation by Attempt identity.
        var budgetRead = await _budgetGate.GetReservationStateAsync(identity, cancellationToken);

        // Step 4: Read governance checkpoint from authoritative provider.
        var checkpointRead = await _auditor.GetPreDispatchStateAsync(identity, cancellationToken);

        // Step 4b: If checkpoint is Accepted, validate the full checkpoint content.
        // The checkpoint must be internally consistent and match the recovery identity.
        // Cross-verify that the checkpoint's lease, approval, and reservation all
        // belong to the same attempt — not just the same tenant/attempt string.
        if (checkpointRead.Status == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            if (checkpointRead.Checkpoint is null || checkpointRead.Receipt is null)
            {
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = AgentToolPreDispatchReconciliationStatus.Conflict
                };
            }

            var checkpoint = checkpointRead.Checkpoint;
            if (checkpoint.Context is null
                || checkpoint.Lease is null
                || checkpoint.Approval is null
                || checkpoint.BudgetReservation is null)
            {
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = AgentToolPreDispatchReconciliationStatus.Conflict
                };
            }

            // Validate identity: checkpoint's AttemptId and LogicalInvocationKey
            // must match the recovery identity.
            if (!AgentToolGovernancePreDispatchComparer.ValidateIdentity(checkpoint, identity))
            {
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = AgentToolPreDispatchReconciliationStatus.Conflict
                };
            }

            if (checkpointRead.Receipt.Identity != identity
                || gateState.Intent is null
                || !AgentToolGovernancePreDispatchComparer.MatchesFrozenIntent(
                    gateState.Intent,
                    checkpoint))
            {
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = AgentToolPreDispatchReconciliationStatus.Conflict
                };
            }

            // Cross-verify: if Budget was read, the checkpoint's reservation
            // AttemptId must match the budget reservation's AttemptId.
            if (budgetRead.Reservation is not null
                && !AgentToolGovernancePreDispatchComparer.ReservationIdentityAndTermsEqual(
                    checkpoint.BudgetReservation,
                    budgetRead.Reservation))
            {
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = AgentToolPreDispatchReconciliationStatus.Conflict
                };
            }

            if (gateState.State is AgentToolInvocationPreDispatchState.Ready
                    or AgentToolInvocationPreDispatchState.Accepted
                && !string.Equals(
                    gateState.BoundReservationId,
                    checkpoint.BudgetReservation.ReservationId,
                    StringComparison.Ordinal))
            {
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = AgentToolPreDispatchReconciliationStatus.Conflict
                };
            }

            if (gateState.State == AgentToolInvocationPreDispatchState.Accepted
                && (gateState.AcceptedReceipt is null
                    || !AgentToolGovernancePreDispatchComparer.Equivalent(
                        gateState.AcceptedReceipt,
                        checkpointRead.Receipt)))
            {
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = AgentToolPreDispatchReconciliationStatus.Conflict
                };
            }

            // Cross-verify: checkpoint's lease AttemptId must match identity.
            if (!string.Equals(
                    checkpoint.Lease?.AttemptId,
                    identity.AttemptId,
                    StringComparison.Ordinal))
            {
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = AgentToolPreDispatchReconciliationStatus.Conflict
                };
            }
        }

        // Step 5: Compose reconciliation decision based on Gate + Budget + Checkpoint.
        var (status, reasonCode) = ComposeStatus(gateState.State, budgetRead.Status, checkpointRead.Status);

        // Step 6: Execute recovery transactions for terminal statuses that require side effects.
        if (status == AgentToolPreDispatchReconciliationStatus.Released)
        {
            var releaseReason = reasonCode;
            var terminalReservation = budgetRead.Reservation;

            // If budget is still Reserved, finalize it to Released.
            if (budgetRead.Status == AgentToolBudgetReadStatus.Reserved && budgetRead.Reservation is not null)
            {
                var finalizeResult = await _budgetGate.FinalizeAsync(
                    new AgentToolBudgetFinalizeRequest
                    {
                        ReservationId = budgetRead.Reservation.ReservationId,
                        AttemptId = identity.AttemptId,
                        InvocationFingerprint = budgetRead.Reservation.InvocationFingerprint,
                        RequestedState = AgentToolBudgetReservationState.Released,
                        ReasonCode = releaseReason
                    },
                    cancellationToken).ConfigureAwait(false);

                // Verify the budget actually reached the requested terminal state.
                if (finalizeResult.State != AgentToolBudgetReservationState.Released)
                {
                    return new AgentToolPreDispatchReconciliationResult
                    {
                        Status = AgentToolPreDispatchReconciliationStatus.Conflict
                    };
                }

                terminalReservation = finalizeResult;
            }

            // A checkpoint is not terminal until the exact Released governance
            // fact is durable. This precedes Gate publication so response loss can
            // safely converge using the same finalization record.
            if (checkpointRead.Status == AgentToolGovernancePreDispatchReadStatus.Accepted)
            {
                if (checkpointRead.Checkpoint is null
                    || checkpointRead.Receipt is null
                    || terminalReservation is null
                    || terminalReservation.State != AgentToolBudgetReservationState.Released)
                {
                    return await CreateTerminalResultAsync(
                        identity,
                        AgentToolPreDispatchReconciliationStatus.Conflict,
                        "governance_finalization_evidence_missing",
                        cancellationToken).ConfigureAwait(false);
                }

                var outcome = new AgentToolInvocationOutcome
                {
                    Kind = AgentToolInvocationOutcomeKind.InProgress,
                    Code = "AGENT_TOOL_INVOCATION_NOT_ACQUIRED",
                    Message = "The tool invocation could not acquire execution ownership."
                };
                var finalization = new AgentToolGovernanceFinalizationRecord
                {
                    AuditId = checkpointRead.Receipt.AuditId,
                    Context = checkpointRead.Checkpoint.Context,
                    Lease = checkpointRead.Checkpoint.Lease,
                    DispatchStarted = false,
                    BudgetReservation = terminalReservation,
                    AttemptState = AgentToolGovernanceAttemptFinalState.Released,
                    InvocationState = null,
                    Outcome = outcome,
                    OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(
                        outcome,
                        Array.Empty<AgentToolAuditFact>()),
                    AuditFacts = Array.Empty<AgentToolAuditFact>(),
                    ReasonCode = releaseReason
                };

                AgentToolGovernanceFinalizationResult auditFinalization;
                try
                {
                    auditFinalization = await _auditor.FinalizeAsync(finalization, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    return await CreateObservationResultAsync(
                        identity,
                        AgentToolPreDispatchReconciliationStatus.StillPending,
                        "governance_finalization_unavailable",
                        cancellationToken).ConfigureAwait(false);
                }

                if (auditFinalization.Status != AgentToolGovernanceFinalizationStatus.Finalized
                    || auditFinalization.Record is null
                    || !AgentToolGovernancePreDispatchComparer.Equivalent(
                        auditFinalization.Record,
                        finalization))
                {
                    return await CreateTerminalResultAsync(
                        identity,
                        AgentToolPreDispatchReconciliationStatus.Conflict,
                        "governance_finalization_conflict",
                        cancellationToken).ConfigureAwait(false);
                }
            }

            // Transition Gate to terminal via identity-based release/abandon.
            if (gateState.State == AgentToolInvocationPreDispatchState.Pending)
            {
                // Unrecorded attempt — abandon the gate.
                var abandonResult = await _gate.AbandonByIdentityAsync(identity, releaseReason, cancellationToken)
                    .ConfigureAwait(false);
                if (abandonResult.State != AgentToolInvocationPreDispatchState.Abandoned)
                {
                    return new AgentToolPreDispatchReconciliationResult
                    {
                        Status = AgentToolPreDispatchReconciliationStatus.Conflict
                    };
                }
            }
            else
            {
                // Accepted → Released.
                var releaseResult = await _gate.ReleaseByIdentityAsync(identity, releaseReason, cancellationToken)
                    .ConfigureAwait(false);
                if (releaseResult.State != AgentToolInvocationPreDispatchState.Released)
                {
                    return new AgentToolPreDispatchReconciliationResult
                    {
                        Status = AgentToolPreDispatchReconciliationStatus.Conflict
                    };
                }
            }
        }

        // Step 7: If terminal, persist immutable receipt and publish accountability.
        if (IsTerminal(status))
        {
            return await CreateTerminalResultAsync(identity, status, reasonCode, cancellationToken);
        }

        // Step 8: StillPending — persist mutable observation.
        return await CreateObservationResultAsync(identity, status, reasonCode, cancellationToken);
    }

    private static (AgentToolPreDispatchReconciliationStatus, string) ComposeStatus(
        AgentToolInvocationPreDispatchState gateState,
        AgentToolBudgetReadStatus budgetStatus,
        AgentToolGovernancePreDispatchReadStatus checkpointStatus)
    {
        // §7.7: Pending + authoritative Budget Missing + authoritative Checkpoint Missing + dispatch false → Abandoned
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && budgetStatus == AgentToolBudgetReadStatus.Missing
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Missing)
        {
            return (AgentToolPreDispatchReconciliationStatus.Released, "abandoned_unrecorded");
        }

        // Pending with budget reserved or checkpoint accepted → StillPending (attempt may still be in-flight)
        // BUT: Pending + Committed → Conflict (budget committed without dispatch)
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && budgetStatus == AgentToolBudgetReadStatus.Committed)
        {
            return (AgentToolPreDispatchReconciliationStatus.Conflict, "budget_committed_no_dispatch");
        }

        // Pending + Accepted checkpoint → Conflict (checkpoint advanced past gate)
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return (AgentToolPreDispatchReconciliationStatus.Conflict, "checkpoint_accepted_but_gate_pending");
        }

        // Pending with budget reserved or checkpoint accepted → StillPending (attempt may still be in-flight)
        if (gateState == AgentToolInvocationPreDispatchState.Pending)
        {
            return (AgentToolPreDispatchReconciliationStatus.StillPending, "pending_in_flight");
        }

        // §7.10: Ready/Accepted + Budget Missing → Conflict
        if (gateState is AgentToolInvocationPreDispatchState.Ready or AgentToolInvocationPreDispatchState.Accepted
            && budgetStatus == AgentToolBudgetReadStatus.Missing)
        {
            return (AgentToolPreDispatchReconciliationStatus.Conflict, "budget_missing_after_bind");
        }

        // §7.9: Accepted + Reserved → release/finalize/publish without dispatch
        if (gateState == AgentToolInvocationPreDispatchState.Accepted
            && budgetStatus == AgentToolBudgetReadStatus.Reserved
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return (AgentToolPreDispatchReconciliationStatus.Released, "released_no_dispatch");
        }

        // §7.8: Accepted checkpoint + Released budget → converge
        if (gateState == AgentToolInvocationPreDispatchState.Accepted
            && budgetStatus == AgentToolBudgetReadStatus.Released
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return (AgentToolPreDispatchReconciliationStatus.Released, "released_no_dispatch");
        }

        // §7.10: Committed budget → Conflict (budget committed without dispatch)
        if (budgetStatus == AgentToolBudgetReadStatus.Committed)
        {
            return (AgentToolPreDispatchReconciliationStatus.Conflict, "budget_committed_no_dispatch");
        }

        // §7.10: Indeterminate budget → StillPending
        if (budgetStatus == AgentToolBudgetReadStatus.Indeterminate)
        {
            return (AgentToolPreDispatchReconciliationStatus.StillPending, "budget_indeterminate");
        }

        // Authority unavailable → StillPending
        if (budgetStatus == AgentToolBudgetReadStatus.Unknown
            || checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Unknown)
        {
            return (AgentToolPreDispatchReconciliationStatus.StillPending, "authority_unavailable");
        }

        return (AgentToolPreDispatchReconciliationStatus.StillPending, "unresolved");
    }

    private static bool IsTerminal(AgentToolPreDispatchReconciliationStatus status)
        => status is AgentToolPreDispatchReconciliationStatus.Released
            or AgentToolPreDispatchReconciliationStatus.Conflict
            or AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown;

    private static AgentToolPreDispatchReconciliationStatus ReplayStatus(
        AgentToolPreDispatchReconciliationReceipt receipt)
        => receipt.Status == AgentToolPreDispatchReconciliationStatus.Released
            ? AgentToolPreDispatchReconciliationStatus.AlreadyReleased
            : receipt.Status;

    private async ValueTask<AgentToolPreDispatchReconciliationResult> CreateTerminalResultAsync(
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchReconciliationStatus status,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var receipt = new AgentToolPreDispatchReconciliationReceipt
        {
            Identity = identity,
            Status = status,
            ReasonCode = reasonCode,
            TerminalAt = now,
            IntegrityValue = ComputeIntegrity(identity, status, reasonCode, now)
        };

        var inserted = await _store.TryInsertReceiptAsync(receipt, cancellationToken);
        if (!inserted)
        {
            // CAS insert failed — another reconciler won the race. Read the existing receipt.
            var existingReceipt = await _store.ReadReceiptAsync(identity, cancellationToken);
            if (existingReceipt is not null)
            {
                // Return the already-persisted receipt, not a new one.
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = ReplayStatus(existingReceipt),
                    Receipt = existingReceipt
                };
            }

            // CAS failed AND no existing receipt found — the store is in an inconsistent state.
            // Do NOT return an unpersisted receipt. Return Indeterminate so the caller knows
            // the terminal state could not be durably established.
            return new AgentToolPreDispatchReconciliationResult
            {
                Status = AgentToolPreDispatchReconciliationStatus.Conflict
            };
        }

        // Receipt was successfully persisted. Publish accountability best-effort.
        // Accountability failure must NOT alter the reconciliation result.
        if (_accountabilityProducer is not null)
        {
            try
            {
                await _accountabilityProducer.PublishAsync(identity, status, reasonCode, cancellationToken);
            }
            catch
            {
                // Accountability failure is observed/logged and cannot alter the reconciliation result.
            }
        }

        return new AgentToolPreDispatchReconciliationResult
        {
            Status = status,
            Receipt = receipt
        };
    }

    private async ValueTask<AgentToolPreDispatchReconciliationResult> CreateObservationResultAsync(
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchReconciliationStatus status,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var existing = await _store.ReadObservationAsync(identity, cancellationToken);
        var newRevision = (existing?.Revision ?? 0) + 1;

        var observation = new AgentToolPreDispatchReconciliationObservation
        {
            Identity = identity,
            Status = status,
            ReasonCode = reasonCode,
            ObservedAt = now,
            Revision = newRevision
        };

        // Check observation CAS result — if the upsert failed (concurrent revision change),
        // return Conflict so the caller knows the observation was not persisted.
        var upserted = await _store.TryUpsertObservationAsync(observation, existing?.Revision ?? 0, cancellationToken);
        if (!upserted)
        {
            return new AgentToolPreDispatchReconciliationResult
            {
                Status = AgentToolPreDispatchReconciliationStatus.Conflict
            };
        }

        return new AgentToolPreDispatchReconciliationResult
        {
            Status = status,
            Observation = observation
        };
    }

    private static string ComputeIntegrity(
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchReconciliationStatus status,
        string reasonCode,
        DateTimeOffset terminalAt)
    {
        return $"{identity.AttemptId}:{identity.LogicalInvocationKey.InvocationId}:{status}:{reasonCode}:{terminalAt:O}";
    }
}

public sealed class NullAgentToolPreDispatchAccountabilityProducer : IAgentToolPreDispatchReconciliationAccountabilityProducer
{
    public ValueTask PublishAsync(
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchReconciliationStatus status,
        string reasonCode,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

/// <summary>
/// Real Accountability producer (activated in Slice 6). Uses IAuditRecorder,
/// never IAuditSink. Emits only safe IDs/descriptors/reason families.
/// Accountability failure is observed/logged and cannot alter the reconciliation result.
/// </summary>
public sealed class AgentToolPreDispatchReconciliationAccountabilityProducer : IAgentToolPreDispatchReconciliationAccountabilityProducer
{
    private readonly IAuditRecorder _auditRecorder;
    private readonly TimeProvider _timeProvider;

    public AgentToolPreDispatchReconciliationAccountabilityProducer(
        IAuditRecorder auditRecorder,
        TimeProvider? timeProvider = null)
    {
        _auditRecorder = auditRecorder ?? throw new ArgumentNullException(nameof(auditRecorder));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask PublishAsync(
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchReconciliationStatus status,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var now = _timeProvider.GetUtcNow();
            var auditId = $"acr-{identity.LogicalInvocationKey.InvocationId}-{identity.AttemptId}-{now:yyyyMMddHHmmssfff}";

            var envelope = new AuditEnvelope
            {
                AuditId = auditId,
                OccurredAt = now,
                TenantId = identity.LogicalInvocationKey.TenantId,
                CorrelationId = identity.LogicalInvocationKey.ExecutionId,
                CausationId = identity.LogicalInvocationKey.InvocationId,
                Actor = new AuditActor
                {
                    Kind = AuditActorKinds.System,
                    Id = "agent-tool-reconciler",
                    DisplayName = "Agent Tool Pre-Dispatch Reconciler"
                },
                Action = new AuditAction
                {
                    Kind = "control.transition",
                    Name = "AgentToolPreDispatchReconciliation"
                },
                Target = new AuditTarget
                {
                    Kind = "agent-tool-invocation",
                    Id = $"{identity.LogicalInvocationKey.InvocationId}:{identity.AttemptId}"
                },
                Outcome = new AuditOutcome
                {
                    Status = status == AgentToolPreDispatchReconciliationStatus.Released
                        ? AuditOutcomeStatuses.Succeeded
                        : AuditOutcomeStatuses.Succeeded,
                    Code = reasonCode
                },
                Tags = AuditTagMap.Empty.Add("reconciliation.status", status.ToString())
            };

            await _auditRecorder.RecordAsync(envelope, cancellationToken);
        }
        catch
        {
            // Accountability failure is observed/logged and cannot alter the reconciliation result.
            // The control terminal/receipt is already persisted; projection may retry independently.
        }
    }
}
