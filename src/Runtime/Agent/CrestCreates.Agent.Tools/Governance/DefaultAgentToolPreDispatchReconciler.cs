namespace CrestCreates.Agent.Tools;

/// <summary>
/// Default runtime-owned reconciler that reads Gate, Budget, and checkpoint
/// in the fixed Spec order (§7.6). Never dispatches, never evaluates approval,
/// never creates budget reservations. The reconciliation settlement (claim Gate
/// ownership, settle budget, finalize governance, publish terminal Gate outcome)
/// is delegated to <see cref="AgentToolPreDispatchSettlementExecutor"/> and the
/// durable result persistence to <see cref="AgentToolPreDispatchResultWriter"/>,
/// keeping this type a thin protocol mainline. The Accountability producer is
/// wired as an optional no-op collaborator until Slice 6.
/// </summary>
public sealed class DefaultAgentToolPreDispatchReconciler : IAgentToolPreDispatchReconciler
{
    private readonly IAgentToolInvocationGate _gate;
    private readonly IAgentToolBudgetGate _budgetGate;
    private readonly IAgentToolGovernanceAuditor _auditor;
    private readonly IAgentToolPreDispatchReconciliationStore _store;
    private readonly AgentToolPreDispatchRecoveryPolicy _recoveryPolicy = new();
    private readonly AgentToolPreDispatchSettlementExecutor _settlementExecutor;
    private readonly AgentToolPreDispatchResultWriter _resultWriter;

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
        _settlementExecutor = new AgentToolPreDispatchSettlementExecutor(gate, budgetGate, auditor);
        _resultWriter = new AgentToolPreDispatchResultWriter(store, timeProvider, accountabilityProducer);
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
            return await _resultWriter.WriteObservationAsync(
                identity, AgentToolPreDispatchReconciliationStatus.StillPending, "gate_missing", cancellationToken);
        }

        if (gateState.State is AgentToolInvocationPreDispatchState.DispatchStarted
            or AgentToolInvocationPreDispatchState.CompletionPending
            or AgentToolInvocationPreDispatchState.Completed)
        {
            return await _resultWriter.WriteTerminalAsync(
                identity, AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown, "dispatch_started", cancellationToken);
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
                    Status = AgentToolPreDispatchResultWriter.ReplayStatus(existingReceipt),
                    Receipt = existingReceipt
                };
            }

            // The Gate terminal CAS is authoritative control evidence. A crash
            // may occur after that commit but before receipt insertion, so create
            // the first immutable receipt instead of freezing a safely closed
            // Attempt as Conflict.
            return await _resultWriter.WriteTerminalAsync(
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
                Status = AgentToolPreDispatchResultWriter.ReplayStatus(priorReceipt),
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
                return await _resultWriter.WriteTerminalAsync(
                    identity,
                    AgentToolPreDispatchReconciliationStatus.Conflict,
                    "checkpoint_evidence_missing",
                    cancellationToken).ConfigureAwait(false);
            }

            var checkpoint = checkpointRead.Checkpoint;
            if (checkpoint.Context is null
                || checkpoint.Lease is null
                || checkpoint.Approval is null
                || checkpoint.BudgetReservation is null)
            {
                return await _resultWriter.WriteTerminalAsync(
                    identity,
                    AgentToolPreDispatchReconciliationStatus.Conflict,
                    "checkpoint_incomplete",
                    cancellationToken).ConfigureAwait(false);
            }

            // Validate identity: checkpoint's AttemptId and LogicalInvocationKey
            // must match the recovery identity.
            if (!AgentToolGovernancePreDispatchComparer.ValidateIdentity(checkpoint, identity))
            {
                return await _resultWriter.WriteTerminalAsync(
                    identity,
                    AgentToolPreDispatchReconciliationStatus.Conflict,
                    "checkpoint_identity_mismatch",
                    cancellationToken).ConfigureAwait(false);
            }

            if (checkpointRead.Receipt.Identity != identity
                || gateState.Intent is null
                || !AgentToolGovernancePreDispatchComparer.MatchesFrozenIntent(
                    gateState.Intent,
                    checkpoint))
            {
                return await _resultWriter.WriteTerminalAsync(
                    identity,
                    AgentToolPreDispatchReconciliationStatus.Conflict,
                    "checkpoint_intent_mismatch",
                    cancellationToken).ConfigureAwait(false);
            }

            // Cross-verify: if Budget was read, the checkpoint's reservation
            // AttemptId must match the budget reservation's AttemptId.
            if (budgetRead.Reservation is not null
                && !AgentToolGovernancePreDispatchComparer.ReservationIdentityAndTermsEqual(
                    checkpoint.BudgetReservation,
                    budgetRead.Reservation))
            {
                return await _resultWriter.WriteTerminalAsync(
                    identity,
                    AgentToolPreDispatchReconciliationStatus.Conflict,
                    "checkpoint_reservation_mismatch",
                    cancellationToken).ConfigureAwait(false);
            }

            if (effectiveGateState is AgentToolInvocationPreDispatchState.Ready
                    or AgentToolInvocationPreDispatchState.Accepted
                && !string.Equals(
                    gateState.BoundReservationId,
                    checkpoint.BudgetReservation.ReservationId,
                    StringComparison.Ordinal))
            {
                return await _resultWriter.WriteTerminalAsync(
                    identity,
                    AgentToolPreDispatchReconciliationStatus.Conflict,
                    "checkpoint_bound_reservation_mismatch",
                    cancellationToken).ConfigureAwait(false);
            }

            if (effectiveGateState == AgentToolInvocationPreDispatchState.Accepted
                && (gateState.AcceptedReceipt is null
                    || !AgentToolGovernancePreDispatchComparer.Equivalent(
                        gateState.AcceptedReceipt,
                        checkpointRead.Receipt)))
            {
                return await _resultWriter.WriteTerminalAsync(
                    identity,
                    AgentToolPreDispatchReconciliationStatus.Conflict,
                    "checkpoint_accepted_receipt_mismatch",
                    cancellationToken).ConfigureAwait(false);
            }

            // Cross-verify: checkpoint's lease AttemptId must match identity.
            if (!string.Equals(
                    checkpoint.Lease?.AttemptId,
                    identity.AttemptId,
                    StringComparison.Ordinal))
            {
                return await _resultWriter.WriteTerminalAsync(
                    identity,
                    AgentToolPreDispatchReconciliationStatus.Conflict,
                    "checkpoint_lease_mismatch",
                    cancellationToken).ConfigureAwait(false);
            }
        }

        // Step 5: Compose the recovery decision from the pure policy. The policy
        // preserves the exact Gate + Budget + Checkpoint composition matrix
        // (including the claimed substate of a ReconciliationPending attempt) and
        // emits the full set of settlement actions consumed by the executor.
        var decision = _recoveryPolicy.Decide(new AgentToolPreDispatchAuthoritySnapshot
        {
            Gate = gateState,
            Budget = budgetRead,
            Checkpoint = checkpointRead
        });
        var status = decision.Disposition;
        var reasonCode = decision.ReasonCode;

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
            var settlement = await _settlementExecutor.ExecuteAsync(
                identity,
                decision,
                new AgentToolPreDispatchAuthoritySnapshot
                {
                    Gate = gateState,
                    Budget = budgetRead,
                    Checkpoint = checkpointRead
                },
                context,
                cancellationToken).ConfigureAwait(false);

            // Route the settlement outcome to durable persistence. Every settlement
            // outcome must carry a durable form: terminal → immutable receipt,
            // retryable → mutable observation. A bare terminal result is never
            // returned by the executor.
            if (settlement.Persistence == AgentToolPreDispatchSettlementPersistence.TerminalReceipt)
            {
                return await _resultWriter.WriteTerminalAsync(
                    identity,
                    settlement.Status,
                    settlement.ReasonCode ?? reasonCode,
                    cancellationToken).ConfigureAwait(false);
            }

            return await _resultWriter.WriteObservationAsync(
                identity,
                settlement.Status,
                settlement.ReasonCode ?? reasonCode,
                cancellationToken).ConfigureAwait(false);
        }

        // Step 7/8: StillPending — persist mutable observation; terminal statuses
        // (Conflict / PostDispatchUnknown) — persist immutable receipt.
        return status == AgentToolPreDispatchReconciliationStatus.StillPending
            ? await _resultWriter.WriteObservationAsync(identity, status, reasonCode, cancellationToken)
            : await _resultWriter.WriteTerminalAsync(identity, status, reasonCode, cancellationToken);
    }
}
