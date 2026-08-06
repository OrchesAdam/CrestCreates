namespace CrestCreates.Agent.Tools;

/// <summary>
/// What to do with the Gate once a recovery decision is made. A terminal
/// decision that touches the Gate must first claim (or recover) reconciliation
/// ownership — the Gate remains the single ownership authority.
/// </summary>
internal enum AgentToolPreDispatchGateAction
{
    /// <summary>No Gate mutation. Used by StillPending and Conflict decisions.</summary>
    None,

    /// <summary>Claim reconciliation ownership, then complete the Gate as Abandoned.</summary>
    ClaimAndAbandon,

    /// <summary>Claim reconciliation ownership, then complete the Gate as Released.</summary>
    ClaimAndRelease
}

/// <summary>What to do with the budget reservation once a recovery decision is made.</summary>
internal enum AgentToolPreDispatchBudgetAction
{
    /// <summary>No Budget mutation.</summary>
    None,

    /// <summary>Finalize the reservation to Released.</summary>
    FinalizeReleased
}

/// <summary>What to do with the governance checkpoint once a recovery decision is made.</summary>
internal enum AgentToolPreDispatchGovernanceAction
{
    /// <summary>No Governance mutation.</summary>
    None,

    /// <summary>Finalize the checkpoint as Released without dispatch.</summary>
    FinalizeReleasedNoDispatch
}

/// <summary>
/// Immutable snapshot of the three pre-dispatch authorities read by the
/// reconciler, in the fixed Spec order: Gate, Budget, Checkpoint.
/// </summary>
internal sealed record AgentToolPreDispatchAuthoritySnapshot
{
    public required AgentToolInvocationPreDispatchResult Gate { get; init; }

    public required AgentToolBudgetReservationReadResult Budget { get; init; }

    public required AgentToolGovernancePreDispatchReadResult Checkpoint { get; init; }

    /// <summary>
    /// The Gate state that drives composition. A ReconciliationPending attempt
    /// was claimed by a prior reconciler; its preserved substate drives the same
    /// decision instead of treating the claim state as an unresolved live worker.
    /// </summary>
    public AgentToolInvocationPreDispatchState EffectiveGateState
        => Gate.State == AgentToolInvocationPreDispatchState.ReconciliationPending
            ? Gate.ReconciliationClaimedState ?? Gate.State
            : Gate.State;
}

/// <summary>
/// Immutable recovery decision describing every intended mutation. Produced by
/// the pure <see cref="AgentToolPreDispatchRecoveryPolicy"/> and consumed by the
/// settlement executor — one place decides, one place settles.
/// </summary>
internal sealed record AgentToolPreDispatchRecoveryDecision
{
    public required AgentToolPreDispatchReconciliationStatus Disposition { get; init; }

    public required AgentToolPreDispatchGateAction GateAction { get; init; }

    public required AgentToolPreDispatchBudgetAction BudgetAction { get; init; }

    public required AgentToolPreDispatchGovernanceAction GovernanceAction { get; init; }

    public required string ReasonCode { get; init; }

    public bool IsTerminal
        => Disposition is AgentToolPreDispatchReconciliationStatus.Released
            or AgentToolPreDispatchReconciliationStatus.Conflict
            or AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown;

    public bool AbandonGate => GateAction == AgentToolPreDispatchGateAction.ClaimAndAbandon;

    /// <summary>
    /// Whether the settlement executor must first acquire (or recover) Gate
    /// reconciliation ownership. Derived from <see cref="GateAction"/> so a
    /// decision can never describe a claim in one place and not in another:
    /// any terminal Gate mutation is claim-gated by construction.
    /// </summary>
    public bool RequiresOwnershipClaim
        => GateAction is AgentToolPreDispatchGateAction.ClaimAndAbandon
            or AgentToolPreDispatchGateAction.ClaimAndRelease;
}

/// <summary>
/// Pure recovery policy: converts an immutable authority snapshot into an
/// immutable recovery decision. Has no provider, DI, time, logging, dispatcher,
/// or store dependencies. Preserves the exact composition matrix ordering and
/// ReasonCodes of the durable protocol.
/// </summary>
internal sealed class AgentToolPreDispatchRecoveryPolicy
{
    public AgentToolPreDispatchRecoveryDecision Decide(AgentToolPreDispatchAuthoritySnapshot snapshot)
    {
        var gateState = snapshot.EffectiveGateState;
        var budgetStatus = snapshot.Budget.Status;
        var checkpointStatus = snapshot.Checkpoint.Status;

        // Terminal / post-dispatch gates can never be recovered by reconciliation.
        // The reconciler mainline short-circuits these before reading Budget and
        // Checkpoint, so this is a defensive total-function guarantee: the policy
        // never proposes a mutation for a gate that already advanced past live
        // recovery.
        if (gateState is AgentToolInvocationPreDispatchState.DispatchStarted
            or AgentToolInvocationPreDispatchState.CompletionPending
            or AgentToolInvocationPreDispatchState.Completed)
        {
            return PostDispatchUnknown("dispatch_started");
        }

        // ── Pending gate ────────────────────────────────────────────────────────────
        // §7.7: Pending + authoritative Budget Missing + authoritative Checkpoint Missing → Abandoned.
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && budgetStatus == AgentToolBudgetReadStatus.Missing
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Missing)
        {
            return Released("abandoned_unrecorded", AgentToolPreDispatchGateAction.ClaimAndAbandon);
        }

        // CW04/CW05: Reserve committed (response lost) or reservation returned before the
        // gate bound it — checkpoint was never recorded. Release the reservation and
        // abandon the attempt.
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && budgetStatus == AgentToolBudgetReadStatus.Reserved
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Missing)
        {
            return Released(
                "budget_reserved_no_checkpoint",
                AgentToolPreDispatchGateAction.ClaimAndAbandon,
                AgentToolPreDispatchBudgetAction.FinalizeReleased);
        }

        // A previous reconciliation already released the reservation but crashed before the
        // gate transition — converge by abandoning the unrecorded attempt.
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && budgetStatus == AgentToolBudgetReadStatus.Released
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Missing)
        {
            return Released("budget_released_no_checkpoint", AgentToolPreDispatchGateAction.ClaimAndAbandon);
        }

        // Pending + Committed → Conflict (budget committed without dispatch)
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && budgetStatus == AgentToolBudgetReadStatus.Committed)
        {
            return Conflict("budget_committed_no_dispatch");
        }

        // Pending + Accepted checkpoint → Conflict (checkpoint advanced past gate)
        if (gateState == AgentToolInvocationPreDispatchState.Pending
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return Conflict("checkpoint_accepted_but_gate_pending");
        }

        // Pending with budget reserved or checkpoint accepted → StillPending (attempt may still be in-flight)
        if (gateState == AgentToolInvocationPreDispatchState.Pending)
        {
            return StillPending("pending_in_flight");
        }

        // ── Ready gate ──────────────────────────────────────────────────────────────
        // CW04/CW05: reservation returned and gate bound it, checkpoint never recorded.
        // Release the reservation and abandon the attempt.
        if (gateState == AgentToolInvocationPreDispatchState.Ready
            && budgetStatus == AgentToolBudgetReadStatus.Reserved
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Missing)
        {
            return Released(
                "budget_reserved_no_checkpoint",
                AgentToolPreDispatchGateAction.ClaimAndAbandon,
                AgentToolPreDispatchBudgetAction.FinalizeReleased);
        }

        // Crash between budget finalize and gate transition for an unrecorded attempt.
        if (gateState == AgentToolInvocationPreDispatchState.Ready
            && budgetStatus == AgentToolBudgetReadStatus.Released
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Missing)
        {
            return Released("budget_released_no_checkpoint", AgentToolPreDispatchGateAction.ClaimAndAbandon);
        }

        // CW07/CW08/CW09: checkpoint committed (response lost) or receipt obtained before
        // the gate advanced. Validate the full checkpoint, finalize governance, release
        // the reservation, and release the attempt without dispatch.
        if (gateState == AgentToolInvocationPreDispatchState.Ready
            && budgetStatus == AgentToolBudgetReadStatus.Reserved
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return Released(
                "released_no_dispatch",
                AgentToolPreDispatchGateAction.ClaimAndRelease,
                AgentToolPreDispatchBudgetAction.FinalizeReleased,
                AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch);
        }

        // Crash between budget finalize and gate transition with a recorded checkpoint.
        if (gateState == AgentToolInvocationPreDispatchState.Ready
            && budgetStatus == AgentToolBudgetReadStatus.Released
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return Released(
                "released_no_dispatch",
                AgentToolPreDispatchGateAction.ClaimAndRelease,
                governanceAction: AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch);
        }

        // §7.10: Ready/Accepted + Budget Missing → Conflict
        if (gateState is AgentToolInvocationPreDispatchState.Ready or AgentToolInvocationPreDispatchState.Accepted
            && budgetStatus == AgentToolBudgetReadStatus.Missing)
        {
            return Conflict("budget_missing_after_bind");
        }

        // ── Accepted gate ───────────────────────────────────────────────────────────
        // §7.9: Accepted + Reserved → release/finalize/publish without dispatch
        if (gateState == AgentToolInvocationPreDispatchState.Accepted
            && budgetStatus == AgentToolBudgetReadStatus.Reserved
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return Released(
                "released_no_dispatch",
                AgentToolPreDispatchGateAction.ClaimAndRelease,
                AgentToolPreDispatchBudgetAction.FinalizeReleased,
                AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch);
        }

        // §7.8: Accepted checkpoint + Released budget → converge
        if (gateState == AgentToolInvocationPreDispatchState.Accepted
            && budgetStatus == AgentToolBudgetReadStatus.Released
            && checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
        {
            return Released(
                "released_no_dispatch",
                AgentToolPreDispatchGateAction.ClaimAndRelease,
                governanceAction: AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch);
        }

        // ── Generic conflict / unavailable ──────────────────────────────────────────
        // §7.10: Committed budget → Conflict (budget committed without dispatch)
        if (budgetStatus == AgentToolBudgetReadStatus.Committed)
        {
            return Conflict("budget_committed_no_dispatch");
        }

        // §7.10: Indeterminate budget → StillPending
        if (budgetStatus == AgentToolBudgetReadStatus.Indeterminate)
        {
            return StillPending("budget_indeterminate");
        }

        // Authority unavailable → StillPending
        if (budgetStatus == AgentToolBudgetReadStatus.Unknown
            || checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Unknown)
        {
            return StillPending("authority_unavailable");
        }

        return StillPending("unresolved");
    }

    private static AgentToolPreDispatchRecoveryDecision Released(
        string reasonCode,
        AgentToolPreDispatchGateAction gateAction,
        AgentToolPreDispatchBudgetAction budgetAction = AgentToolPreDispatchBudgetAction.None,
        AgentToolPreDispatchGovernanceAction governanceAction = AgentToolPreDispatchGovernanceAction.None)
        => new()
        {
            Disposition = AgentToolPreDispatchReconciliationStatus.Released,
            GateAction = gateAction,
            BudgetAction = budgetAction,
            GovernanceAction = governanceAction,
            ReasonCode = reasonCode
        };

    private static AgentToolPreDispatchRecoveryDecision Conflict(string reasonCode)
        => new()
        {
            Disposition = AgentToolPreDispatchReconciliationStatus.Conflict,
            GateAction = AgentToolPreDispatchGateAction.None,
            BudgetAction = AgentToolPreDispatchBudgetAction.None,
            GovernanceAction = AgentToolPreDispatchGovernanceAction.None,
            ReasonCode = reasonCode
        };

    private static AgentToolPreDispatchRecoveryDecision PostDispatchUnknown(string reasonCode)
        => new()
        {
            Disposition = AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown,
            GateAction = AgentToolPreDispatchGateAction.None,
            BudgetAction = AgentToolPreDispatchBudgetAction.None,
            GovernanceAction = AgentToolPreDispatchGovernanceAction.None,
            ReasonCode = reasonCode
        };

    private static AgentToolPreDispatchRecoveryDecision StillPending(string reasonCode)
        => new()
        {
            Disposition = AgentToolPreDispatchReconciliationStatus.StillPending,
            GateAction = AgentToolPreDispatchGateAction.None,
            BudgetAction = AgentToolPreDispatchBudgetAction.None,
            GovernanceAction = AgentToolPreDispatchGovernanceAction.None,
            ReasonCode = reasonCode
        };
}
