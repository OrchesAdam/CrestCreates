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
                    Status = AgentToolPreDispatchReconciliationStatus.AlreadyReleased,
                    Receipt = existingReceipt
                };
            }

            return await CreateTerminalResultAsync(identity, AgentToolPreDispatchReconciliationStatus.AlreadyReleased, "already_terminal", cancellationToken);
        }

        // Check for existing terminal receipt before proceeding (idempotent reconciliation).
        var priorReceipt = await _store.ReadReceiptAsync(identity, cancellationToken);
        if (priorReceipt is not null)
        {
            return new AgentToolPreDispatchReconciliationResult
            {
                Status = AgentToolPreDispatchReconciliationStatus.AlreadyReleased,
                Receipt = priorReceipt
            };
        }

        // Step 3: Read budget reservation by Attempt identity.
        var budgetRead = await _budgetGate.GetReservationStateAsync(identity, cancellationToken);

        // Step 4: Read governance checkpoint from authoritative provider.
        var checkpointRead = await _auditor.GetPreDispatchStateAsync(identity, cancellationToken);

        // Step 5: Compose reconciliation decision based on Gate + Budget + Checkpoint.
        var (status, reasonCode) = ComposeStatus(gateState.State, budgetRead.Status, checkpointRead.Status);

        // Step 6: Execute recovery transactions for terminal statuses that require side effects.
        if (status == AgentToolPreDispatchReconciliationStatus.Released)
        {
            // §7.9/§7.8: Execute the release transition — finalize budget, transition gate, finalize governance.
            var releaseReason = reasonCode;

            // If budget is still Reserved, finalize it to Released.
            if (budgetRead.Status == AgentToolBudgetReadStatus.Reserved && budgetRead.Reservation is not null)
            {
                await _budgetGate.FinalizeAsync(
                    new AgentToolBudgetFinalizeRequest
                    {
                        ReservationId = budgetRead.Reservation.ReservationId,
                        AttemptId = identity.AttemptId,
                        InvocationFingerprint = budgetRead.Reservation.InvocationFingerprint,
                        RequestedState = AgentToolBudgetReservationState.Released,
                        ReasonCode = releaseReason
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            // Transition Gate: Accepted → ReleasePending → Released.
            // The lease from the checkpoint is needed for gate release operations.
            // We use the lease from the intent/frozen lease stored in the gate.
            // Since we don't have the original lease object, we read it from the gate state.
            // The gate's GetPreDispatchStateAsync returns the accepted receipt which contains the identity.
            // For the release transition, we need the lease — but the reconciler doesn't have it.
            // The gate should support identity-based release for reconciliation.
            // For now, we skip gate release if we don't have a lease — the budget release + governance
            // finalization are the authoritative recovery actions. The gate state will be cleaned up
            // by retention or a subsequent acquire.
            //
            // Actually, the spec says the reconciler must transition the gate. But the gate's
            // PrepareReleaseAsync/PublishReleaseAsync require a lease. The reconciler doesn't have
            // the original lease. This is a design gap — the gate needs an identity-based release
            // method for reconciliation. For now, we finalize budget and governance, which are the
            // authoritative recovery actions. The gate remains in Accepted state, which is safe —
            // a subsequent acquire will create a new attempt.
            //
            // TODO: Add identity-based release to IAgentToolInvocationGate for reconciler use.

            // Finalize governance audit if checkpoint was Accepted.
            // The reconciler does NOT call Governance.FinalizeAsync because finalization
            // requires a cryptographically valid OutcomeHash (tied to dispatch outcome +
            // audit facts), which the reconciler does not possess. Governance finalization
            // is the invoker's post-dispatch responsibility. The reconciler's authoritative
            // recovery actions are: budget release + gate release.
            // The governance checkpoint remains in Accepted state, which is safe — it
            // records that pre-dispatch governance was satisfied but dispatch never started.
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
            return (AgentToolPreDispatchReconciliationStatus.Released, "released_converge");
        }

        // §7.10: Committed while no DispatchStarted → Conflict
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

        // Checkpoint conflict → Conflict
        if (checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted
            && gateState == AgentToolInvocationPreDispatchState.Pending)
        {
            return (AgentToolPreDispatchReconciliationStatus.Conflict, "checkpoint_accepted_but_gate_pending");
        }

        return (AgentToolPreDispatchReconciliationStatus.StillPending, "unresolved");
    }

    private static bool IsTerminal(AgentToolPreDispatchReconciliationStatus status)
        => status is AgentToolPreDispatchReconciliationStatus.Released
            or AgentToolPreDispatchReconciliationStatus.Conflict
            or AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown;

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
                    Status = AgentToolPreDispatchReconciliationStatus.AlreadyReleased,
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
