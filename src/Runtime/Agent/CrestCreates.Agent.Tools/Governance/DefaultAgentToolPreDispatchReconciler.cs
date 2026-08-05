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
        CancellationToken cancellationToken = default,
        AgentToolPreDispatchReconciliationContext? context = null)
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

        // A ReconciliationPending attempt was claimed by a prior reconciler that
        // crashed before completing the reconciliation. The preserved claimed substate
        // (Pending/Ready/Accepted) drives checkpoint validation and status composition,
        // so the next reconciler converges the same decision instead of treating the
        // claim state as an unresolved live worker.
        var effectiveGateState = gateState.State == AgentToolInvocationPreDispatchState.ReconciliationPending
            ? gateState.ReconciliationClaimedState ?? gateState.State
            : gateState.State;

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

            if (effectiveGateState is AgentToolInvocationPreDispatchState.Ready
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

            if (effectiveGateState == AgentToolInvocationPreDispatchState.Accepted
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
        // When a claim is being recovered, the preserved substate drives composition.
        var (status, reasonCode, abandonGate) = ComposeStatus(effectiveGateState, budgetRead.Status, checkpointRead.Status);

        // Step 6: Execute recovery transactions for terminal statuses that require side effects.
        // The Gate decides who owns the Attempt BEFORE any budget or governance mutation.
        // A granted reconciliation claim is the durable ownership fence: it invalidates
        // the live worker's lease/fencing token and moves the Attempt to
        // ReconciliationPending, so a still-alive Invoker can never win
        // TryMarkDispatchStarted after the claim. Only then does the reconciler settle
        // the budget, finalize governance, and publish the terminal Gate outcome
        // through the claim.
        if (status == AgentToolPreDispatchReconciliationStatus.Released)
        {
            var releaseReason = reasonCode;
            var terminalReservation = budgetRead.Reservation;

            // A. Claim Gate ownership — or recover a claim granted by a prior
            // reconciler that crashed before completing the reconciliation.
            AgentToolPreDispatchReconciliationClaim? claim;
            if (gateState.State == AgentToolInvocationPreDispatchState.ReconciliationPending)
            {
                claim = BuildRecoveredClaim(identity, gateState);
            }
            else
            {
                var claimResult = await _gate.TryBeginPreDispatchReconciliationAsync(
                    new AgentToolPreDispatchReconciliationClaimRequest
                    {
                        Identity = identity,
                        ExpectedRevision = gateState.Revision,
                        OwnershipLost = context?.OwnershipLost ?? false,
                        OwnershipEvidence = context?.OwnershipEvidence
                    },
                    cancellationToken).ConfigureAwait(false);

                if (claimResult.Status != AgentToolPreDispatchReconciliationClaimStatus.Claimed)
                {
                    // No claim was granted. Re-read the Attempt to distinguish
                    // "live worker still owns it" from "a competing transition won".
                    // Budget and governance are NOT touched in either case.
                    var current = await _gate.GetPreDispatchStateAsync(identity, cancellationToken)
                        .ConfigureAwait(false);
                    if (current.State == AgentToolInvocationPreDispatchState.DispatchStarted)
                    {
                        return await CreateTerminalResultAsync(
                            identity,
                            AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown,
                            "dispatch_started",
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (current.State is AgentToolInvocationPreDispatchState.Released
                        or AgentToolInvocationPreDispatchState.Abandoned)
                    {
                        return await CreateTerminalResultAsync(
                            identity,
                            AgentToolPreDispatchReconciliationStatus.Released,
                            current.ReasonCode ?? "terminal_recovered",
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (current.State == AgentToolInvocationPreDispatchState.ReconciliationPending)
                    {
                        // A prior reconciler claimed but crashed before completing.
                        claim = BuildRecoveredClaim(identity, current);
                    }
                    else
                    {
                        // The Attempt is still Pending/Ready/Accepted with a live
                        // lease and no ownership-loss proof — the worker may still
                        // be running. Do NOT abandon it and do NOT release its budget.
                        return await CreateObservationResultAsync(
                            identity,
                            AgentToolPreDispatchReconciliationStatus.StillPending,
                            claimResult.ReasonCode ?? "ownership_not_lost",
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    claim = claimResult.Claim;
                }
            }

            // B. If budget is still Reserved, finalize it to Released. The claim is
            // held, so the live worker can no longer transition the Attempt or commit
            // the reservation.
            if (budgetRead.Status == AgentToolBudgetReadStatus.Reserved && budgetRead.Reservation is not null)
            {
                var finalizeResult = await _budgetGate.FinalizeAsync(
                    new AgentToolBudgetFinalizeRequest
                    {
                        ReservationId = budgetRead.Reservation.ReservationId,
                        TenantId = identity.LogicalInvocationKey.TenantId,
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

            // C. A checkpoint is not terminal until the exact Released governance
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

            // D. Publish the terminal Gate outcome through the claim. Abandon when the
            // checkpoint was never recorded (attempt did not exist durably); release
            // otherwise. Only the claim token (not the lease/fencing token, which the
            // claim invalidated) authorizes this transition.
            var completionKind = abandonGate
                ? AgentToolPreDispatchReconciliationCompletionKind.Abandoned
                : AgentToolPreDispatchReconciliationCompletionKind.Released;
            var terminalState = abandonGate
                ? AgentToolInvocationPreDispatchState.Abandoned
                : AgentToolInvocationPreDispatchState.Released;
            if (claim is null)
            {
                // A claim could not be granted or recovered — no terminal transition.
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = AgentToolPreDispatchReconciliationStatus.Conflict
                };
            }
            var completeResult = await _gate.CompletePreDispatchReconciliationAsync(
                claim,
                completionKind,
                releaseReason,
                cancellationToken).ConfigureAwait(false);
            if (completeResult.State != terminalState)
            {
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = AgentToolPreDispatchReconciliationStatus.Conflict
                };
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

    private static (AgentToolPreDispatchReconciliationStatus Status, string ReasonCode, bool AbandonGate) ComposeStatus(
        AgentToolInvocationPreDispatchState gateState,
        AgentToolBudgetReadStatus budgetStatus,
        AgentToolGovernancePreDispatchReadStatus checkpointStatus)
    {
        // ── Pending gate ────────────────────────────────────────────────────────────
        // §7.7: Pending + authoritative Budget Missing + authoritative Checkpoint Missing → Abandoned.
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && budgetStatus == AgentToolBudgetReadStatus.Missing
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Missing)
        {
            return (AgentToolPreDispatchReconciliationStatus.Released, "abandoned_unrecorded", AbandonGate: true);
        }

        // CW04/CW05: Reserve committed (response lost) or reservation returned before the
        // gate bound it — checkpoint was never recorded. Release the reservation and
        // abandon the attempt.
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && budgetStatus == AgentToolBudgetReadStatus.Reserved
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Missing)
        {
            return (AgentToolPreDispatchReconciliationStatus.Released, "budget_reserved_no_checkpoint", AbandonGate: true);
        }

        // A previous reconciliation already released the reservation but crashed before the
        // gate transition — converge by abandoning the unrecorded attempt.
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && budgetStatus == AgentToolBudgetReadStatus.Released
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Missing)
        {
            return (AgentToolPreDispatchReconciliationStatus.Released, "budget_released_no_checkpoint", AbandonGate: true);
        }

        // Pending + Committed → Conflict (budget committed without dispatch)
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && budgetStatus == AgentToolBudgetReadStatus.Committed)
        {
            return (AgentToolPreDispatchReconciliationStatus.Conflict, "budget_committed_no_dispatch", AbandonGate: false);
        }

        // Pending + Accepted checkpoint → Conflict (checkpoint advanced past gate)
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return (AgentToolPreDispatchReconciliationStatus.Conflict, "checkpoint_accepted_but_gate_pending", AbandonGate: false);
        }

        // Pending with budget reserved or checkpoint accepted → StillPending (attempt may still be in-flight)
        if (gateState == AgentToolInvocationPreDispatchState.Pending)
        {
            return (AgentToolPreDispatchReconciliationStatus.StillPending, "pending_in_flight", AbandonGate: false);
        }

        // ── Ready gate ──────────────────────────────────────────────────────────────
        // CW04/CW05: reservation returned and gate bound it, checkpoint never recorded.
        // Release the reservation and abandon the attempt.
        if (gateState == AgentToolInvocationPreDispatchState.Ready
            && budgetStatus == AgentToolBudgetReadStatus.Reserved
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Missing)
        {
            return (AgentToolPreDispatchReconciliationStatus.Released, "budget_reserved_no_checkpoint", AbandonGate: true);
        }

        // Crash between budget finalize and gate transition for an unrecorded attempt.
        if (gateState == AgentToolInvocationPreDispatchState.Ready
            && budgetStatus == AgentToolBudgetReadStatus.Released
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Missing)
        {
            return (AgentToolPreDispatchReconciliationStatus.Released, "budget_released_no_checkpoint", AbandonGate: true);
        }

        // CW07/CW08/CW09: checkpoint committed (response lost) or receipt obtained before
        // the gate advanced. Validate the full checkpoint, finalize governance, release
        // the reservation, and release the attempt without dispatch.
        if (gateState == AgentToolInvocationPreDispatchState.Ready
            && budgetStatus == AgentToolBudgetReadStatus.Reserved
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return (AgentToolPreDispatchReconciliationStatus.Released, "released_no_dispatch", AbandonGate: false);
        }

        // Crash between budget finalize and gate transition with a recorded checkpoint.
        if (gateState == AgentToolInvocationPreDispatchState.Ready
            && budgetStatus == AgentToolBudgetReadStatus.Released
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return (AgentToolPreDispatchReconciliationStatus.Released, "released_no_dispatch", AbandonGate: false);
        }

        // §7.10: Ready/Accepted + Budget Missing → Conflict
        if (gateState is AgentToolInvocationPreDispatchState.Ready or AgentToolInvocationPreDispatchState.Accepted
            && budgetStatus == AgentToolBudgetReadStatus.Missing)
        {
            return (AgentToolPreDispatchReconciliationStatus.Conflict, "budget_missing_after_bind", AbandonGate: false);
        }

        // ── Accepted gate ───────────────────────────────────────────────────────────
        // §7.9: Accepted + Reserved → release/finalize/publish without dispatch
        if (gateState == AgentToolInvocationPreDispatchState.Accepted
            && budgetStatus == AgentToolBudgetReadStatus.Reserved
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return (AgentToolPreDispatchReconciliationStatus.Released, "released_no_dispatch", AbandonGate: false);
        }

        // §7.8: Accepted checkpoint + Released budget → converge
        if (gateState == AgentToolInvocationPreDispatchState.Accepted
            && budgetStatus == AgentToolBudgetReadStatus.Released
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return (AgentToolPreDispatchReconciliationStatus.Released, "released_no_dispatch", AbandonGate: false);
        }

        // ── Generic conflict / unavailable ──────────────────────────────────────────
        // §7.10: Committed budget → Conflict (budget committed without dispatch)
        if (budgetStatus == AgentToolBudgetReadStatus.Committed)
        {
            return (AgentToolPreDispatchReconciliationStatus.Conflict, "budget_committed_no_dispatch", AbandonGate: false);
        }

        // §7.10: Indeterminate budget → StillPending
        if (budgetStatus == AgentToolBudgetReadStatus.Indeterminate)
        {
            return (AgentToolPreDispatchReconciliationStatus.StillPending, "budget_indeterminate", AbandonGate: false);
        }

        // Authority unavailable → StillPending
        if (budgetStatus == AgentToolBudgetReadStatus.Unknown
            || checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Unknown)
        {
            return (AgentToolPreDispatchReconciliationStatus.StillPending, "authority_unavailable", AbandonGate: false);
        }

        return (AgentToolPreDispatchReconciliationStatus.StillPending, "unresolved", AbandonGate: false);
    }

    /// <summary>
    /// Reconstructs an immutable reconciliation claim from a Gate read that already
    /// shows <see cref="AgentToolInvocationPreDispatchState.ReconciliationPending"/>.
    /// Used when a prior reconciler claimed the Attempt but crashed before completing
    /// the reconciliation: the durable claim token and preserved substate let the
    /// next reconciler finish without a second (conflicting) claim.
    /// </summary>
    private static AgentToolPreDispatchReconciliationClaim BuildRecoveredClaim(
        AgentToolPreDispatchIdentity identity,
        AgentToolInvocationPreDispatchResult gateState)
        => new()
        {
            Identity = identity,
            Revision = gateState.Revision,
            ClaimToken = gateState.ReconciliationClaimToken
                ?? throw new InvalidOperationException(
                    "A ReconciliationPending attempt must carry a reconciliation claim token."),
            ClaimedAt = gateState.ReconciliationClaimedAt ?? DateTimeOffset.MinValue,
            ClaimedState = gateState.ReconciliationClaimedState
                ?? AgentToolInvocationPreDispatchState.Pending,
            Indeterminate = gateState.Indeterminate,
            BoundReservationId = gateState.BoundReservationId,
            AcceptedReceipt = gateState.AcceptedReceipt,
            Intent = gateState.Intent,
            LastReasonCode = gateState.ReasonCode
        };

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
                    // P1-05: map reconciliation outcomes to accountability semantics.
                    // Released → succeeded; Conflict → rejected; PostDispatchUnknown and
                    // StillPending → indeterminate (the attempt did not fail, but its
                    // terminal disposition is not confirmable).
                    Status = status switch
                    {
                        AgentToolPreDispatchReconciliationStatus.Released => AuditOutcomeStatuses.Succeeded,
                        AgentToolPreDispatchReconciliationStatus.Conflict => AuditOutcomeStatuses.Rejected,
                        AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown => AuditOutcomeStatuses.Indeterminate,
                        _ => AuditOutcomeStatuses.Indeterminate
                    },
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
