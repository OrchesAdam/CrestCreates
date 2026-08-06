namespace CrestCreates.Agent.Tools;

/// <summary>
/// How the durable result of a settlement outcome must be persisted.
/// <list type="bullet">
/// <item><see cref="AgentToolPreDispatchSettlementPersistence.TerminalReceipt"/> — immutable terminal receipt.</item>
/// <item><see cref="AgentToolPreDispatchSettlementPersistence.Observation"/> — mutable revision-CAS observation (retryable).</item>
/// </list>
/// A settlement outcome always persists its durable result: terminal dispositions
/// carry an immutable receipt, retryable dispositions carry an observation. A bare
/// result with no durable write is not an allowed protocol outcome.
/// </summary>
internal enum AgentToolPreDispatchSettlementPersistence
{
    Observation,
    TerminalReceipt
}

/// <summary>
/// Result of a reconciliation settlement attempt. The reconciler mainline routes
/// to the result writer based on <see cref="Persistence"/>:
/// terminal receipt for released / durable conflict / post-dispatch outcomes,
/// mutable observation for still-pending outcomes (ownership not lost, authority
/// unavailable).
/// </summary>
internal sealed record AgentToolPreDispatchSettlementResult
{
    public required AgentToolPreDispatchReconciliationStatus Status { get; init; }

    public string? ReasonCode { get; init; }

    public AgentToolPreDispatchSettlementPersistence Persistence { get; init; }
}

/// <summary>
/// Executes the settlement phase of the pre-dispatch reconciliation protocol:
/// claim (or recover) Gate ownership, settle the budget reservation, finalize
/// governance, and publish the terminal Gate outcome through the claim. The Gate
/// decides ownership before any Budget or Governance mutation, so a still-alive
/// Invoker can never win <c>TryMarkDispatchStarted</c> after the claim.
/// Dependencies are exactly the three authorities — no Dispatcher, no store, no
/// accountability producer.
/// </summary>
internal sealed class AgentToolPreDispatchSettlementExecutor
{
    private readonly IAgentToolInvocationGate _gate;
    private readonly IAgentToolBudgetGate _budgetGate;
    private readonly IAgentToolGovernanceAuditor _auditor;

    public AgentToolPreDispatchSettlementExecutor(
        IAgentToolInvocationGate gate,
        IAgentToolBudgetGate budgetGate,
        IAgentToolGovernanceAuditor auditor)
    {
        _gate = gate;
        _budgetGate = budgetGate;
        _auditor = auditor;
    }

    public async ValueTask<AgentToolPreDispatchSettlementResult> ExecuteAsync(
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchRecoveryDecision decision,
        AgentToolPreDispatchAuthoritySnapshot snapshot,
        AgentToolPreDispatchReconciliationContext? context,
        CancellationToken cancellationToken = default)
    {
        var gateState = snapshot.Gate;
        var budgetRead = snapshot.Budget;
        var checkpointRead = snapshot.Checkpoint;
        var releaseReason = decision.ReasonCode;
        var terminalReservation = budgetRead.Reservation;
        var abandonGate = decision.AbandonGate;

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
                    return Terminal(
                        AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown,
                        "dispatch_started");
                }

                if (current.State is AgentToolInvocationPreDispatchState.Released
                    or AgentToolInvocationPreDispatchState.Abandoned)
                {
                    return Terminal(
                        AgentToolPreDispatchReconciliationStatus.Released,
                        current.ReasonCode ?? "terminal_recovered");
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
                    // The reconciliation-level disposition is always "ownership
                    // not lost"; the claim layer's own refusal reason is internal.
                    return Observation("ownership_not_lost");
                }
            }
            else
            {
                claim = claimResult.Claim;
            }
        }

        // B. If the policy requires settling the reservation, finalize it to
        // Released. The claim is held, so the live worker can no longer
        // transition the Attempt or commit the reservation.
        if (decision.BudgetAction == AgentToolPreDispatchBudgetAction.FinalizeReleased
            && budgetRead.Reservation is not null)
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
            // A mismatched finalization is never a bare conflict: a reservation that
            // moved to another terminal state is a deterministic conflict (durable
            // receipt), while a reservation that is still Reserved/Unknown means the
            // authority did not confirm the release (retryable observation).
            if (finalizeResult.State != AgentToolBudgetReservationState.Released)
            {
                if (finalizeResult.State is AgentToolBudgetReservationState.Committed
                    or AgentToolBudgetReservationState.Indeterminate)
                {
                    return Terminal(
                        AgentToolPreDispatchReconciliationStatus.Conflict,
                        "budget_finalize_conflict");
                }

                return Observation("budget_finalize_unconfirmed");
            }

            terminalReservation = finalizeResult;
        }

        // C. A checkpoint is not terminal until the exact Released governance
        // fact is durable. This precedes Gate publication so response loss can
        // safely converge using the same finalization record. Only runs when the
        // policy demands a Released-no-dispatch finalization (checkpoint Accepted).
        if (decision.GovernanceAction == AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch)
        {
            if (checkpointRead.Checkpoint is null
                || checkpointRead.Receipt is null
                || terminalReservation is null
                || terminalReservation.State != AgentToolBudgetReservationState.Released)
            {
                return Terminal(
                    AgentToolPreDispatchReconciliationStatus.Conflict,
                    "governance_finalization_evidence_missing");
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
                return Observation("governance_finalization_unavailable");
            }

            if (auditFinalization.Status != AgentToolGovernanceFinalizationStatus.Finalized
                || auditFinalization.Record is null
                || !AgentToolGovernancePreDispatchComparer.Equivalent(
                    auditFinalization.Record,
                    finalization))
            {
                return Terminal(
                    AgentToolPreDispatchReconciliationStatus.Conflict,
                    "governance_finalization_conflict");
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
            // Ownership was not established, so this is a retryable authority
            // outcome (observation), never a bare terminal result.
            return Observation("reconciliation_claim_unavailable");
        }

        var completeResult = await _gate.CompletePreDispatchReconciliationAsync(
            claim,
            completionKind,
            releaseReason,
            cancellationToken).ConfigureAwait(false);
        if (completeResult.State != terminalState)
        {
            // The gate did not reach the requested terminal outcome. Classify the
            // actual outcome instead of returning a bare conflict: a live worker may
            // have won dispatch (PostDispatchUnknown), the gate may already be in
            // the other terminal state (Released), or the completion did not take
            // (retryable observation while the claim is still held).
            if (completeResult.State == AgentToolInvocationPreDispatchState.DispatchStarted)
            {
                return Terminal(
                    AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown,
                    "dispatch_started");
            }

            if (completeResult.State is AgentToolInvocationPreDispatchState.Released
                or AgentToolInvocationPreDispatchState.Abandoned)
            {
                return Terminal(
                    AgentToolPreDispatchReconciliationStatus.Released,
                    completeResult.ReasonCode ?? "terminal_recovered");
            }

            if (completeResult.State == AgentToolInvocationPreDispatchState.ReconciliationPending)
            {
                return Observation("reconciliation_completion_unconfirmed");
            }

            return Terminal(
                AgentToolPreDispatchReconciliationStatus.Conflict,
                "reconciliation_completion_conflict");
        }

        return new AgentToolPreDispatchSettlementResult
        {
            Status = AgentToolPreDispatchReconciliationStatus.Released,
            ReasonCode = releaseReason,
            Persistence = AgentToolPreDispatchSettlementPersistence.TerminalReceipt
        };
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

    private static AgentToolPreDispatchSettlementResult Terminal(
        AgentToolPreDispatchReconciliationStatus status,
        string reasonCode)
        => new()
        {
            Status = status,
            ReasonCode = reasonCode,
            Persistence = AgentToolPreDispatchSettlementPersistence.TerminalReceipt
        };

    private static AgentToolPreDispatchSettlementResult Observation(string reasonCode)
        => new()
        {
            Status = AgentToolPreDispatchReconciliationStatus.StillPending,
            ReasonCode = reasonCode,
            Persistence = AgentToolPreDispatchSettlementPersistence.Observation
        };
}
