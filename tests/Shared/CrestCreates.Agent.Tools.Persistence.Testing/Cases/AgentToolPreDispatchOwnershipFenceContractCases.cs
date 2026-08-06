using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Agent.Tools.Persistence.Testing.Assertions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools.Persistence.Testing.Cases;

/// <summary>
/// Context needed by the ownership-fence shared contract cases. Each runner
/// (InMemory, PostgreSQL) wires its own real participants plus a provider
/// specific lease-expiry hook so the cases exercise the same semantics on
/// every provider.
/// </summary>
public sealed class AgentToolPreDispatchOwnershipFenceContext
{
    public required IAgentToolInvocationGate Gate { get; init; }

    /// <summary>
    /// Optional second participant used by the true-race cases (for example a
    /// second ServiceProvider over the same durable schema, or the same gate
    /// when the provider linearizes internally). When omitted, races run both
    /// participants against <see cref="Gate"/>.
    /// </summary>
    public IAgentToolInvocationGate? SecondGate { get; init; }

    public required IAgentToolBudgetGate BudgetGate { get; init; }

    public required IAgentToolGovernanceAuditor Auditor { get; init; }

    public required IAgentToolPreDispatchReconciler Reconciler { get; init; }

    public required IAgentToolPreDispatchReconciliationStore ReconciliationStore { get; init; }

    /// <summary>
    /// Provider-specific lease expiry. The InMemory runner advances its manual
    /// clock; the PostgreSQL runner issues an UPDATE against the backend. The
    /// identity carries the tenant so durable runners keep tenant scope.
    /// </summary>
    public required Func<AgentToolPreDispatchIdentity, AgentToolInvocationLease, ValueTask>
        ExpireLeaseAsync { get; init; }
}

/// <summary>
/// Shared semantic contract cases for the reconciliation ownership fence
/// (review P0): MarkIndeterminate must fence every live-worker forward
/// transition, and the Reconciler must claim Gate ownership BEFORE settling
/// Budget or Governance. Activated by concrete runners for InMemory and
/// PostgreSQL.
/// </summary>
public static class AgentToolPreDispatchOwnershipFenceContractCases
{
    private sealed class Setup
    {
        public required AgentToolLogicalInvocationKey Key { get; init; }

        public required string Fingerprint { get; init; }

        public required AgentToolInvocationLease Lease { get; init; }

        public AgentToolPreDispatchIdentity Identity
            => new(Key, Lease.AttemptId);
    }

    private const string BoundReservationId = "res-owner-fence";

    private static readonly AgentToolPreDispatchReconciliationContext OwnershipLostContext
        = new()
        {
            OwnershipLost = true,
            OwnershipEvidence = "test-asserts-worker-dead"
        };

    // ── Indeterminate fencing (gate-only) ────────────────────────────────────

    public static async Task IndeterminatePending_Should_Reject_ReservationBind(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var setup = await AcquirePendingAsync(ctx, cancellationToken);
        await ctx.Gate.MarkIndeterminateAsync(setup.Lease, "fenced", cancellationToken);

        var bindResult = await TryBindReservationAsync(ctx, setup, cancellationToken);
        var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);

        // The sub-state is preserved for recovery; the forward transition is rejected.
        AgentToolPreDispatchContractAssertions.True(
            bindResult.State != AgentToolInvocationPreDispatchState.Ready,
            $"Indeterminate Pending attempt bound a reservation ({bindResult.State}).");
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationPreDispatchState.Pending,
            state.State,
            "Indeterminate must preserve the Pending recovery sub-state and reject reservation bind.");
    }

    public static async Task IndeterminateReady_Should_Reject_AcceptedBind(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, _) = await SetupReadyAsync(ctx, cancellationToken);
        await ctx.Gate.MarkIndeterminateAsync(setup.Lease, "fenced", cancellationToken);

        var bindResult = await TryBindAcceptedAsync(ctx, setup, cancellationToken);
        var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);

        AgentToolPreDispatchContractAssertions.True(
            bindResult.State != AgentToolInvocationPreDispatchState.Accepted,
            $"Indeterminate Ready attempt bound an accepted receipt ({bindResult.State}).");
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationPreDispatchState.Ready,
            state.State,
            "Indeterminate must preserve the Ready recovery sub-state and reject accepted bind.");
    }

    public static async Task IndeterminateAccepted_Should_Reject_DispatchStarted(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, reservation, receipt) = await SetupAcceptedAsync(ctx, cancellationToken);
        await ctx.Gate.MarkIndeterminateAsync(setup.Lease, "fenced", cancellationToken);

        var dispatched = await ctx.Gate.TryMarkDispatchStartedAsync(
            setup.Lease, receipt, reservation.ReservationId, cancellationToken);
        var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);

        AgentToolPreDispatchContractAssertions.True(
            !dispatched,
            "Indeterminate fenced Attempt must never transition to DispatchStarted (INV-09).");
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationPreDispatchState.Accepted,
            state.State,
            "Indeterminate must preserve the Accepted recovery sub-state.");
    }

    // ── Live worker safety (reconciler must NOT claim a live Attempt) ────────

    public static async Task LivePending_Reconcile_Should_Not_AbandonActiveWorker(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var setup = await AcquirePendingAsync(ctx, cancellationToken);
        await ReserveBudgetAsync(ctx, setup, cancellationToken);

        var result = await ctx.Reconciler.ReconcileAsync(setup.Identity, cancellationToken, context: null);
        var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);
        var budget = await ctx.BudgetGate.GetReservationStateAsync(setup.Identity, cancellationToken);

        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolPreDispatchReconciliationStatus.StillPending,
            result.Status,
            "A live Pending worker must not be abandoned.");
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationPreDispatchState.Pending,
            state.State,
            "A live Pending attempt must not be abandoned.");
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolBudgetReadStatus.Reserved,
            budget.Status,
            "A live worker's budget must not be released.");
    }

    public static async Task LiveReady_Reconcile_Should_Not_ReleaseActiveWorkerBudget(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, _) = await SetupReadyAsync(ctx, cancellationToken);

        var result = await ctx.Reconciler.ReconcileAsync(setup.Identity, cancellationToken, context: null);
        var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);
        var budget = await ctx.BudgetGate.GetReservationStateAsync(setup.Identity, cancellationToken);

        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolPreDispatchReconciliationStatus.StillPending,
            result.Status,
            "A live Ready worker must not be converged without ownership loss.");
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationPreDispatchState.Ready,
            state.State,
            "A live Ready attempt must not be transitioned.");
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolBudgetReadStatus.Reserved,
            budget.Status,
            "A live Ready worker's budget must not be released before Gate ownership is claimed.");
    }

    // ── Claimable reconciliation converges (expired lease / indeterminate) ───

    public static async Task ExpiredLease_Reconcile_Should_ClaimAndConverge(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, _, _) = await SetupAcceptedAsync(ctx, cancellationToken);
        await ctx.ExpireLeaseAsync(setup.Identity, setup.Lease);

        var result = await ctx.Reconciler.ReconcileAsync(setup.Identity, cancellationToken, context: null);
        await AssertReleasedConvergenceAsync(ctx, setup.Identity, result, cancellationToken);
    }

    public static async Task MarkedIndeterminate_Reconcile_Should_ClaimAndConverge(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, _, _) = await SetupAcceptedAsync(ctx, cancellationToken);
        await ctx.Gate.MarkIndeterminateAsync(setup.Lease, "fenced", cancellationToken);

        var result = await ctx.Reconciler.ReconcileAsync(setup.Identity, cancellationToken, context: null);
        await AssertReleasedConvergenceAsync(ctx, setup.Identity, result, cancellationToken);
    }

    // ── Claim wins (deterministic) ───────────────────────────────────────────

    public static async Task ReconciliationClaimWins_Should_BlockDispatch(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, reservation, receipt) = await SetupAcceptedAsync(ctx, cancellationToken);

        var result = await ctx.Reconciler.ReconcileAsync(
            setup.Identity, cancellationToken, OwnershipLostContext);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolPreDispatchReconciliationStatus.Released,
            result.Status,
            "Ownership-lost reconciliation must converge to Released.");

        var dispatched = await ctx.Gate.TryMarkDispatchStartedAsync(
            setup.Lease, receipt, reservation.ReservationId, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            !dispatched,
            "Once the reconciliation claim owns the Attempt, the worker must be fenced.");
    }

    public static async Task ReconciliationClaimWins_Should_ReleaseBudgetExactlyOnce(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, _, _) = await SetupAcceptedAsync(ctx, cancellationToken);

        var first = await ctx.Reconciler.ReconcileAsync(
            setup.Identity, cancellationToken, OwnershipLostContext);
        var budget = await ctx.BudgetGate.GetReservationStateAsync(setup.Identity, cancellationToken);

        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolPreDispatchReconciliationStatus.Released,
            first.Status,
            "First reconciliation must converge to Released.");
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolBudgetReadStatus.Released,
            budget.Status,
            "Budget must be released exactly once by the first reconciliation.");

        // A second reconciliation is idempotent: it replays the terminal receipt
        // and must not finalize the already-released budget a second time.
        var second = await ctx.Reconciler.ReconcileAsync(setup.Identity, cancellationToken);
        var budgetAfter = await ctx.BudgetGate.GetReservationStateAsync(setup.Identity, cancellationToken);

        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolPreDispatchReconciliationStatus.AlreadyReleased,
            second.Status,
            "A repeated reconciliation must replay the terminal receipt.");
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolBudgetReadStatus.Released,
            budgetAfter.Status,
            "Budget must stay released after a repeated reconciliation.");
    }

    public static async Task ReconciliationClaimWins_Should_FinalizeGovernanceExactlyOnce(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, _, receipt) = await SetupAcceptedAsync(ctx, cancellationToken);

        var first = await ctx.Reconciler.ReconcileAsync(
            setup.Identity, cancellationToken, OwnershipLostContext);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolPreDispatchReconciliationStatus.Released,
            first.Status,
            "First reconciliation must converge to Released.");

        var finalization = await ctx.Auditor.GetFinalizationStateAsync(
            receipt.AuditId, setup.Key.TenantId, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolGovernanceFinalizationStatus.Finalized,
            finalization.Status,
            "Governance must be finalized as Released.");
        AgentToolPreDispatchContractAssertions.True(
            finalization.Record is not null,
            "Finalization record must exist.");
        AgentToolPreDispatchContractAssertions.True(
            !finalization.Record!.DispatchStarted,
            "Reconciliation finalization must not claim dispatch started.");
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolGovernanceAttemptFinalState.Released,
            finalization.Record!.AttemptState,
            "Governance attempt state must be Released.");

        var firstRecord = finalization.Record;

        var second = await ctx.Reconciler.ReconcileAsync(setup.Identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolPreDispatchReconciliationStatus.AlreadyReleased,
            second.Status,
            "A repeated reconciliation must replay the terminal receipt.");

        var after = await ctx.Auditor.GetFinalizationStateAsync(
            receipt.AuditId, setup.Key.TenantId, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolGovernanceFinalizationStatus.Finalized,
            after.Status,
            "Governance must remain finalized.");
        AgentToolPreDispatchContractAssertions.True(
            after.Record is not null
                && AgentToolGovernancePreDispatchComparer.Equivalent(after.Record!, firstRecord),
            "Reconciliation must finalize governance exactly once with the same released fact.");
    }

    // ── Dispatch wins (deterministic) ────────────────────────────────────────

    public static async Task DispatchWins_Should_Not_ReleaseBudget(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, reservation, receipt) = await SetupAcceptedAsync(ctx, cancellationToken);

        var dispatched = await ctx.Gate.TryMarkDispatchStartedAsync(
            setup.Lease, receipt, reservation.ReservationId, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            dispatched,
            "Dispatch must win the fence with a live lease.");

        var reconcile = await ctx.Reconciler.ReconcileAsync(
            setup.Identity, cancellationToken, OwnershipLostContext);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown,
            reconcile.Status,
            "A dispatch-started Attempt is PostDispatchUnknown for the reconciler.");

        var budget = await ctx.BudgetGate.GetReservationStateAsync(setup.Identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolBudgetReadStatus.Reserved,
            budget.Status,
            "Dispatch winning the fence must leave the worker's budget untouched.");
    }

    public static async Task DispatchWins_Should_Not_FinalizeGovernanceAsReleased(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, reservation, receipt) = await SetupAcceptedAsync(ctx, cancellationToken);

        var dispatched = await ctx.Gate.TryMarkDispatchStartedAsync(
            setup.Lease, receipt, reservation.ReservationId, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            dispatched,
            "Dispatch must win the fence with a live lease.");

        var reconcile = await ctx.Reconciler.ReconcileAsync(
            setup.Identity, cancellationToken, OwnershipLostContext);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown,
            reconcile.Status,
            "A dispatch-started Attempt is PostDispatchUnknown for the reconciler.");

        var finalization = await ctx.Auditor.GetFinalizationStateAsync(
            receipt.AuditId, setup.Key.TenantId, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolGovernanceFinalizationStatus.NotFinalized,
            finalization.Status,
            "Dispatch winning the fence must never finalize governance as Released.");
    }

    // ── True race: single winner ─────────────────────────────────────────────

    public static async Task LiveAccepted_DispatchAndReconcileRace_Should_HaveSingleWinner(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, reservation, receipt) = await SetupAcceptedAsync(ctx, cancellationToken);

        var dispatchTask = ctx.Gate.TryMarkDispatchStartedAsync(
            setup.Lease, receipt, reservation.ReservationId, cancellationToken).AsTask();
        var reconcileTask = ctx.Reconciler.ReconcileAsync(
            setup.Identity, cancellationToken, OwnershipLostContext).AsTask();

        await Task.WhenAll(dispatchTask, reconcileTask);
        var dispatched = dispatchTask.Result;
        var reconcile = reconcileTask.Result;

        var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);
        var budget = await ctx.BudgetGate.GetReservationStateAsync(setup.Identity, cancellationToken);

        if (state.State == AgentToolInvocationPreDispatchState.DispatchStarted)
        {
            // Dispatch won the fence: the worker's budget and governance remain untouched.
            AgentToolPreDispatchContractAssertions.True(
                dispatched,
                "Dispatch reported true must agree with the DispatchStarted state.");
            AgentToolPreDispatchContractAssertions.Equal(
                AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown,
                reconcile.Status,
                "Reconciliation must observe DispatchStarted as PostDispatchUnknown.");
            AgentToolPreDispatchContractAssertions.Equal(
                AgentToolBudgetReadStatus.Reserved,
                budget.Status,
                "Budget must remain reserved when dispatch wins.");
        }
        else
        {
            // Reconciliation won the fence: dispatch is blocked and the Attempt converged.
            AgentToolPreDispatchContractAssertions.True(
                !dispatched,
                "Dispatch must be blocked when reconciliation wins the fence.");
            AgentToolPreDispatchContractAssertions.Equal(
                AgentToolPreDispatchReconciliationStatus.Released,
                reconcile.Status,
                "Reconciliation must converge to Released.");
            AgentToolPreDispatchContractAssertions.Equal(
                AgentToolInvocationPreDispatchState.Released,
                state.State,
                "Gate must converge to Released.");
            AgentToolPreDispatchContractAssertions.Equal(
                AgentToolBudgetReadStatus.Released,
                budget.Status,
                "Budget must be released when reconciliation wins.");
        }
    }

    // ── MarkIndeterminate atomic CAS (review P0, round 3) ────────────────────

    /// <summary>
    /// A published Completion is a terminal receipt: a later
    /// MarkIndeterminate must be rejected as a single linearized CAS, never
    /// silently succeeding or stamping a marker onto the completed row
    /// (INV-14).
    /// </summary>
    public static async Task PublishedCompletion_Should_Never_Accept_LateIndeterminateMutation(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, reservation, receipt) = await SetupAcceptedAsync(ctx, cancellationToken);
        await PublishCompletedAsync(ctx, setup, reservation, receipt, cancellationToken);

        await AssertThrowsInvalidOperationAsync(
            () => ctx.Gate.MarkIndeterminateAsync(setup.Lease, "late-fence", cancellationToken),
            "MarkIndeterminate must reject a published Completion (INV-14).");

        var completion = await ctx.Gate.GetCompletionStateAsync(setup.Lease, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationCompletionState.Completed,
            completion.State,
            "A published completion must remain the terminal receipt after a late fence attempt.");

        var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            !state.Indeterminate,
            "A late fence must not stamp an indeterminate marker onto a published completion.");
    }

    /// <summary>
    /// A published Release is a terminal receipt: a later MarkIndeterminate
    /// must be rejected as a single linearized CAS (INV-14).
    /// </summary>
    public static async Task PublishedRelease_Should_Never_Accept_LateIndeterminateMutation(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, reservation, _) = await SetupAcceptedAsync(ctx, cancellationToken);
        await PublishReleasedAsync(ctx, setup, reservation, cancellationToken);

        await AssertThrowsInvalidOperationAsync(
            () => ctx.Gate.MarkIndeterminateAsync(setup.Lease, "late-fence", cancellationToken),
            "MarkIndeterminate must reject a published Release (INV-14).");

        var release = await ctx.Gate.GetReleaseStateAsync(setup.Lease, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationReleaseState.Released,
            release.State,
            "A published release must remain the terminal receipt after a late fence attempt.");

        var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            !state.Indeterminate,
            "A late fence must not stamp an indeterminate marker onto a published release.");
    }

    /// <summary>
    /// A CompletionPending row marked indeterminate must read as Indeterminate
    /// (not CompletionPending), while the fence itself reports success.
    /// </summary>
    public static async Task CompletionPending_WithIndeterminateMarker_Should_ReadAsIndeterminate(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, reservation, receipt) = await SetupAcceptedAsync(ctx, cancellationToken);
        var dispatched = await ctx.Gate.TryMarkDispatchStartedAsync(
            setup.Lease, receipt, reservation.ReservationId, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            dispatched,
            "Dispatch must start before completion can be prepared.");
        await ctx.Gate.PrepareCompletionAsync(setup.Lease,
            CompletionRequest(reservation, "audit-mark-completion-pending"), cancellationToken);

        var pending = await ctx.Gate.GetCompletionStateAsync(setup.Lease, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationCompletionState.CompletionPending,
            pending.State,
            "Precondition: completion must be pending before the fence.");

        await ctx.Gate.MarkIndeterminateAsync(setup.Lease, "fenced", cancellationToken);

        var after = await ctx.Gate.GetCompletionStateAsync(setup.Lease, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationCompletionState.Indeterminate,
            after.State,
            "A marked CompletionPending row must read as Indeterminate, not CompletionPending.");

        var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            state.Indeterminate,
            "The indeterminate marker must be visible on the preserved sub-state.");
    }

    /// <summary>
    /// A ReleasePending row marked indeterminate must read as Indeterminate
    /// (not ReleasePending), while the fence itself reports success.
    /// </summary>
    public static async Task ReleasePending_WithIndeterminateMarker_Should_ReadAsIndeterminate(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, reservation, _) = await SetupAcceptedAsync(ctx, cancellationToken);
        await ctx.Gate.PrepareReleaseAsync(setup.Lease,
            ReleaseRequest(reservation, "audit-mark-release-pending"), cancellationToken);

        var pending = await ctx.Gate.GetReleaseStateAsync(setup.Lease, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationReleaseState.ReleasePending,
            pending.State,
            "Precondition: release must be pending before the fence.");

        await ctx.Gate.MarkIndeterminateAsync(setup.Lease, "fenced", cancellationToken);

        var after = await ctx.Gate.GetReleaseStateAsync(setup.Lease, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationReleaseState.Indeterminate,
            after.State,
            "A marked ReleasePending row must read as Indeterminate, not ReleasePending.");

        var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            state.Indeterminate,
            "The indeterminate marker must be visible on the preserved sub-state.");
    }

    /// <summary>
    /// Re-marking an already indeterminate invocation is idempotent success on
    /// every provider (the InMemory provider returns under the same lock; the
    /// PostgreSQL provider classifies its zero-row CAS as an existing fence).
    /// </summary>
    public static async Task MarkIndeterminate_Is_Idempotent_On_ExistingMarker(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, _, _) = await SetupAcceptedAsync(ctx, cancellationToken);

        await ctx.Gate.MarkIndeterminateAsync(setup.Lease, "fenced-first", cancellationToken);
        await ctx.Gate.MarkIndeterminateAsync(setup.Lease, "fenced-second", cancellationToken);

        var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            state.Indeterminate,
            "The marker must be established by the first fence.");
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationPreDispatchState.Accepted,
            state.State,
            "The Accepted recovery sub-state must be preserved by an idempotent re-fence.");
    }

    /// <summary>
    /// When MarkIndeterminate's CAS affects zero rows because a competing
    /// ownership transition won (here: a reconciliation claim that bumps the
    /// fencing token and revision), the call must fail loudly — a zero-row
    /// UPDATE must never be reported as success.
    /// </summary>
    public static async Task MarkIndeterminate_ZeroAffectedRows_Should_Not_ReportSuccess(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, _, _) = await SetupAcceptedAsync(ctx, cancellationToken);
        var current = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);

        var claim = await ctx.Gate.TryBeginPreDispatchReconciliationAsync(
            new AgentToolPreDispatchReconciliationClaimRequest
            {
                Identity = setup.Identity,
                ExpectedRevision = current.Revision,
                OwnershipLost = true,
                OwnershipEvidence = "zero-row-fence-test"
            }, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolPreDispatchReconciliationClaimStatus.Claimed,
            claim.Status,
            "The reconciliation claim must win the fence before the stale MarkIndeterminate.");

        await AssertThrowsInvalidOperationAsync(
            () => ctx.Gate.MarkIndeterminateAsync(setup.Lease, "late-fence", cancellationToken),
            "A MarkIndeterminate whose CAS affects zero rows must not report success.");

        var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            !state.Indeterminate,
            "A lost MarkIndeterminate CAS must not stamp the marker.");
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationPreDispatchState.ReconciliationPending,
            state.State,
            "The claim owner's transition must remain authoritative.");
    }

    /// <summary>
    /// MarkIndeterminate racing DispatchStarted has a linearizable order: a
    /// winning dispatch is observable as DispatchStarted; a winning fence
    /// blocks dispatch and preserves the Accepted recovery sub-state.
    /// </summary>
    public static async Task MarkIndeterminate_vs_DispatchStarted_Should_HaveLinearizableOrder(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var gateA = ctx.Gate;
        var gateB = ctx.SecondGate ?? ctx.Gate;

        for (var round = 0; round < 3; round++)
        {
            var (setup, reservation, receipt) = await SetupAcceptedAsync(ctx, cancellationToken);

            var markTask = CaptureAsync(
                () => gateA.MarkIndeterminateAsync(setup.Lease, "fenced", cancellationToken));
            var dispatchTask = CaptureResultAsync(() => gateB.TryMarkDispatchStartedAsync(
                setup.Lease, receipt, reservation.ReservationId, cancellationToken));
            await Task.WhenAll(markTask, dispatchTask).ConfigureAwait(false);

            var markError = markTask.Result;
            var dispatched = dispatchTask.Result.Result == true;
            var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);

            if (dispatched)
            {
                AgentToolPreDispatchContractAssertions.Equal(
                    AgentToolInvocationPreDispatchState.DispatchStarted,
                    state.State,
                    "A winning dispatch must be observable as DispatchStarted.");
                if (markError is null)
                {
                    AgentToolPreDispatchContractAssertions.True(
                        state.Indeterminate,
                        "A fence that landed after dispatch must be observable on the DispatchStarted row.");
                }
            }
            else
            {
                AgentToolPreDispatchContractAssertions.True(
                    markError is null,
                    "A fenced dispatch implies the fence itself succeeded (INV-09).");
                AgentToolPreDispatchContractAssertions.True(
                    state.Indeterminate,
                    "A fenced dispatch must carry the indeterminate marker.");
                AgentToolPreDispatchContractAssertions.Equal(
                    AgentToolInvocationPreDispatchState.Accepted,
                    state.State,
                    "Indeterminate must preserve the Accepted recovery sub-state.");
            }
        }
    }

    /// <summary>
    /// MarkIndeterminate racing PublishCompletion has exactly one winner: the
    /// published Completion rejects a late fence, and a winning fence makes
    /// PublishCompletion observe Indeterminate — never both terminal
    /// receipts.
    /// </summary>
    public static async Task MarkIndeterminate_vs_PublishCompletion_Should_HaveSingleWinner(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var gateA = ctx.Gate;
        var gateB = ctx.SecondGate ?? ctx.Gate;

        for (var round = 0; round < 3; round++)
        {
            var (setup, reservation, receipt) = await SetupAcceptedAsync(ctx, cancellationToken);
            var dispatched = await ctx.Gate.TryMarkDispatchStartedAsync(
                setup.Lease, receipt, reservation.ReservationId, cancellationToken);
            AgentToolPreDispatchContractAssertions.True(
                dispatched,
                "Dispatch must start before completion publication can race.");
            await ctx.Gate.PrepareCompletionAsync(setup.Lease,
                CompletionRequest(reservation, "audit-mark-completion-race"), cancellationToken);

            var markTask = CaptureAsync(
                () => gateA.MarkIndeterminateAsync(setup.Lease, "fenced", cancellationToken));
            var publishTask = CaptureResultAsync(
                () => gateB.PublishCompletionAsync(setup.Lease, cancellationToken));
            await Task.WhenAll(markTask, publishTask).ConfigureAwait(false);

            var markError = markTask.Result;
            var (published, publishError) = publishTask.Result;

            var completion = await ctx.Gate.GetCompletionStateAsync(setup.Lease, cancellationToken);
            var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);

            if (published is { State: AgentToolInvocationCompletionState.Completed })
            {
                AgentToolPreDispatchContractAssertions.True(
                    markError is not null,
                    "MarkIndeterminate must not silently succeed over a published Completion.");
                AgentToolPreDispatchContractAssertions.Equal(
                    AgentToolInvocationCompletionState.Completed,
                    completion.State,
                    "The published completion must remain the terminal receipt.");
                AgentToolPreDispatchContractAssertions.True(
                    !state.Indeterminate,
                    "A winning publication must not carry a late indeterminate marker.");
            }
            else
            {
                // The fence won first: it reports success, and the losing
                // publish must not reach the terminal state.
                AgentToolPreDispatchContractAssertions.True(
                    markError is null,
                    "The winning fence must report success.");
                AgentToolPreDispatchContractAssertions.True(
                    publishError is null || published is null,
                    "The losing publish must fail or observe the fence, not the terminal state.");
                AgentToolPreDispatchContractAssertions.Equal(
                    AgentToolInvocationCompletionState.Indeterminate,
                    completion.State,
                    "A marked CompletionPending row must read as Indeterminate.");
                AgentToolPreDispatchContractAssertions.True(
                    state.Indeterminate,
                    "The winning fence marker must be visible.");
            }
        }
    }

    /// <summary>
    /// MarkIndeterminate racing PublishRelease has exactly one winner: the
    /// published Release rejects a late fence, and a winning fence makes
    /// PublishRelease observe Indeterminate — never both terminal receipts.
    /// </summary>
    public static async Task MarkIndeterminate_vs_PublishRelease_Should_HaveSingleWinner(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var gateA = ctx.Gate;
        var gateB = ctx.SecondGate ?? ctx.Gate;

        for (var round = 0; round < 3; round++)
        {
            var (setup, reservation, _) = await SetupAcceptedAsync(ctx, cancellationToken);
            await ctx.Gate.PrepareReleaseAsync(setup.Lease,
                ReleaseRequest(reservation, "audit-mark-release-race"), cancellationToken);

            var markTask = CaptureAsync(
                () => gateA.MarkIndeterminateAsync(setup.Lease, "fenced", cancellationToken));
            var publishTask = CaptureResultAsync(
                () => gateB.PublishReleaseAsync(setup.Lease, cancellationToken));
            await Task.WhenAll(markTask, publishTask).ConfigureAwait(false);

            var markError = markTask.Result;
            var (published, publishError) = publishTask.Result;

            var release = await ctx.Gate.GetReleaseStateAsync(setup.Lease, cancellationToken);
            var state = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);

            if (published is { State: AgentToolInvocationReleaseState.Released })
            {
                AgentToolPreDispatchContractAssertions.True(
                    markError is not null,
                    "MarkIndeterminate must not silently succeed over a published Release.");
                AgentToolPreDispatchContractAssertions.Equal(
                    AgentToolInvocationReleaseState.Released,
                    release.State,
                    "The published release must remain the terminal receipt.");
                AgentToolPreDispatchContractAssertions.True(
                    !state.Indeterminate,
                    "A winning release must not carry a late indeterminate marker.");
            }
            else
            {
                // The fence won first: it reports success, and the losing
                // publish must not reach the terminal state.
                AgentToolPreDispatchContractAssertions.True(
                    markError is null,
                    "The winning fence must report success.");
                AgentToolPreDispatchContractAssertions.True(
                    publishError is null || published is null,
                    "The losing publish must fail or observe the fence, not the terminal state.");
                AgentToolPreDispatchContractAssertions.Equal(
                    AgentToolInvocationReleaseState.Indeterminate,
                    release.State,
                    "A marked ReleasePending row must read as Indeterminate.");
                AgentToolPreDispatchContractAssertions.True(
                    state.Indeterminate,
                    "The winning fence marker must be visible.");
            }
        }
    }

    // ── Setup helpers ────────────────────────────────────────────────────────

    private static AgentToolLogicalInvocationKey NewKey(string seed)
        => new(
            $"tenant-{seed}",
            "user-1",
            "agent-1",
            $"exec-{seed}",
            "inv-1");

    private static AgentToolEffectiveGovernance SampleGovernance()
        => new(
            AgentToolSelectionPolicy.ExplicitOnly,
            AgentToolSideEffectKind.InternalWrite,
            CapabilityRiskLevel.Medium,
            AgentToolApprovalMode.Required,
            new AgentToolBudgetRequirement
            {
                Category = "default",
                CostUnits = 1,
                MaxCallsPerExecution = 10
            },
            AgentToolAuditMode.Required);

    private static AgentToolGovernanceAuditContext SampleAuditContext(
        AgentToolLogicalInvocationKey key,
        string attemptId,
        string fingerprint)
        => new()
        {
            LogicalInvocationKey = key,
            AttemptId = attemptId,
            InvocationFingerprint = fingerprint,
            ArgumentsHash = "args-fence",
            ArgumentsEvaluated = true,
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            AgentRolesHash = "roles-fence",
            ToolContract = new AgentToolContractIdentity("tool-fence", 1, "tool-hash"),
            CapabilityContract = new AgentToolContractIdentity("cap-fence", 1, "cap-hash"),
            InputSchemaContract = new AgentToolSchemaContractIdentity("schema-in", 1, "in-hash"),
            OutputSchemaContract = new AgentToolSchemaContractIdentity("schema-out", 1, "out-hash"),
            Governance = SampleGovernance()
        };

    private static AgentToolGovernanceContext SampleGovernanceContext(
        AgentToolLogicalInvocationKey key,
        string attemptId,
        string fingerprint)
        => new()
        {
            LogicalInvocationKey = key,
            AttemptId = attemptId,
            InvocationFingerprint = fingerprint,
            ArgumentsHash = "args-fence",
            ArgumentsEvaluated = true,
            ExecutionContext = new AgentExecutionContext
            {
                ExecutionId = key.ExecutionId,
                InvocationId = key.InvocationId,
                AgentId = key.AgentId,
                AgentRoles = new HashSet<string> { "role-1" },
                CallOrigin = AgentToolCallOrigin.ExplicitRequest
            },
            ToolContract = new AgentToolContractIdentity("tool-fence", 1, "tool-hash"),
            CapabilityContract = new AgentToolContractIdentity("cap-fence", 1, "cap-hash"),
            InputSchemaContract = new AgentToolSchemaContractIdentity("schema-in", 1, "in-hash"),
            OutputSchemaContract = new AgentToolSchemaContractIdentity("schema-out", 1, "out-hash"),
            Governance = SampleGovernance()
        };

    private static AgentToolApprovalResult SampleApproval()
        => new()
        {
            Decision = AgentToolApprovalDecision.Approved,
            ClaimState = AgentToolApprovalEvidenceClaimState.Claimed,
            EvidenceId = "evidence-fence",
            ApproverReference = "approver-fence",
            ReasonCode = "approved-by-policy"
        };

    private static async Task<Setup> AcquirePendingAsync(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var seed = Guid.NewGuid().ToString("N");
        var key = NewKey(seed);
        var fingerprint = $"fp-{seed}";
        var acquired = await ctx.Gate.AcquireAsync(
            new AgentToolInvocationAcquireRequest(key, fingerprint), cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            acquired.Status == AgentToolInvocationAcquireStatus.Acquired
                && acquired.Lease is not null,
            $"Acquire failed: {acquired.Status}.");

        var auditContext = SampleAuditContext(key, acquired.Lease!.AttemptId, fingerprint);
        await ctx.Gate.PreparePreDispatchIntentAsync(acquired.Lease,
            new AgentToolInvocationPreparePreDispatchIntentRequest
            {
                Intent = new AgentToolInvocationPreDispatchIntentSnapshot
                {
                    FrozenLease = acquired.Lease,
                    InvocationFingerprint = fingerprint,
                    Context = auditContext,
                    Approval = SampleApproval()
                }
            }, cancellationToken);

        return new Setup
        {
            Key = key,
            Fingerprint = fingerprint,
            Lease = acquired.Lease
        };
    }

    private static async Task<AgentToolBudgetReservation> ReserveBudgetAsync(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        Setup setup,
        CancellationToken cancellationToken)
    {
        var budgetContext = SampleGovernanceContext(
            setup.Key, setup.Lease.AttemptId, setup.Fingerprint);
        var reserve = await ctx.BudgetGate.ReserveAsync(
            new AgentToolBudgetReserveRequest { Context = budgetContext }, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            reserve.Status == AgentToolBudgetReserveStatus.Reserved
                && reserve.Reservation is not null,
            $"Budget reserve failed: {reserve.Status}.");
        return reserve.Reservation!;
    }

    private static async Task<(Setup Setup, AgentToolBudgetReservation Reservation)> SetupReadyAsync(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var setup = await AcquirePendingAsync(ctx, cancellationToken);
        var reservation = await ReserveBudgetAsync(ctx, setup, cancellationToken);

        var bind = await ctx.Gate.BindPreDispatchReservationAsync(setup.Lease,
            new AgentToolInvocationBindReservationRequest
            {
                ReservationId = reservation.ReservationId,
                Reservation = reservation
            }, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationPreDispatchState.Ready,
            bind.State,
            $"Bind reservation failed: {bind.State}.");

        return (setup, reservation);
    }

    private static async Task<(Setup Setup, AgentToolBudgetReservation Reservation,
        AgentToolGovernancePreDispatchReceipt Receipt)> SetupAcceptedAsync(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        CancellationToken cancellationToken)
    {
        var (setup, reservation) = await SetupReadyAsync(ctx, cancellationToken);

        var readState = await ctx.Gate.GetPreDispatchStateAsync(setup.Identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            readState.Intent is not null,
            "intent missing");

        var record = new AgentToolGovernancePreDispatchRecord
        {
            Context = readState.Intent!.Context,
            Lease = readState.Intent.FrozenLease,
            Approval = readState.Intent.Approval,
            BudgetReservation = reservation
        };
        var write = await ctx.Auditor.RecordPreDispatchAsync(record, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            write.Status == AgentToolGovernancePreDispatchWriteStatus.Accepted
                && write.Receipt is not null,
            $"Checkpoint record failed: {write.Status}.");

        var bind = await ctx.Gate.BindAcceptedPreDispatchAsync(setup.Lease,
            new AgentToolInvocationBindPreDispatchRequest { Receipt = write.Receipt! }, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationPreDispatchState.Accepted,
            bind.State,
            $"Bind accepted failed: {bind.State}.");

        return (setup, reservation, write.Receipt!);
    }

    private static async Task<AgentToolInvocationPreDispatchResult> TryBindReservationAsync(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        Setup setup,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ctx.Gate.BindPreDispatchReservationAsync(setup.Lease,
                new AgentToolInvocationBindReservationRequest
                {
                    ReservationId = BoundReservationId,
                    Reservation = new AgentToolBudgetReservation
                    {
                        ReservationId = BoundReservationId,
                        AttemptId = setup.Lease.AttemptId,
                        InvocationFingerprint = setup.Fingerprint,
                        Category = "default",
                        CostUnits = 1,
                        MaxCallsPerExecution = 10,
                        State = AgentToolBudgetReservationState.Reserved
                    }
                }, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // InMemory providers reject a stale lease by throwing; PostgreSQL
            // returns a non-Ready result. Both are fences.
            return new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Unknown,
                ReasonCode = "fenced"
            };
        }
    }

    private static async Task<AgentToolInvocationPreDispatchResult> TryBindAcceptedAsync(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        Setup setup,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ctx.Gate.BindAcceptedPreDispatchAsync(setup.Lease,
                new AgentToolInvocationBindPreDispatchRequest
                {
                    Receipt = new AgentToolGovernancePreDispatchReceipt
                    {
                        Identity = setup.Identity,
                        AuditId = "audit-fence",
                        AcceptedAt = DateTimeOffset.UtcNow
                    }
                }, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Unknown,
                ReasonCode = "fenced"
            };
        }
    }

    private static async Task AssertReleasedConvergenceAsync(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchReconciliationResult result,
        CancellationToken cancellationToken)
    {
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolPreDispatchReconciliationStatus.Released,
            result.Status,
            $"Expected Released convergence, got {result.Status}.");
        AgentToolPreDispatchContractAssertions.True(
            result.Receipt is not null,
            "Released reconciliation must persist a terminal receipt.");

        var state = await ctx.Gate.GetPreDispatchStateAsync(identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationPreDispatchState.Released,
            state.State,
            "Gate must converge to Released.");

        var budget = await ctx.BudgetGate.GetReservationStateAsync(identity, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolBudgetReadStatus.Released,
            budget.Status,
            "Budget must be released.");
    }

    private static async Task PublishCompletedAsync(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        Setup setup,
        AgentToolBudgetReservation reservation,
        AgentToolGovernancePreDispatchReceipt receipt,
        CancellationToken cancellationToken)
    {
        var dispatched = await ctx.Gate.TryMarkDispatchStartedAsync(
            setup.Lease, receipt, reservation.ReservationId, cancellationToken);
        AgentToolPreDispatchContractAssertions.True(
            dispatched,
            "Dispatch must start before completion publication.");
        await ctx.Gate.PrepareCompletionAsync(setup.Lease,
            CompletionRequest(reservation, "audit-mark-completion-late"), cancellationToken);
        var published = await ctx.Gate.PublishCompletionAsync(setup.Lease, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationCompletionState.Completed,
            published.State,
            "Completion publication must reach the terminal state.");
    }

    private static async Task PublishReleasedAsync(
        AgentToolPreDispatchOwnershipFenceContext ctx,
        Setup setup,
        AgentToolBudgetReservation reservation,
        CancellationToken cancellationToken)
    {
        await ctx.Gate.PrepareReleaseAsync(setup.Lease,
            ReleaseRequest(reservation, "audit-mark-release-late"), cancellationToken);
        var published = await ctx.Gate.PublishReleaseAsync(setup.Lease, cancellationToken);
        AgentToolPreDispatchContractAssertions.Equal(
            AgentToolInvocationReleaseState.Released,
            published.State,
            "Release publication must reach the terminal state.");
    }

    private static AgentToolInvocationPrepareCompletionRequest CompletionRequest(
        AgentToolBudgetReservation reservation,
        string auditId)
        => new()
        {
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.Succeeded,
                Code = "tool-ok",
                Message = "tool completed"
            },
            AuditId = auditId,
            BudgetReservationId = reservation.ReservationId,
            ReasonCode = "tool-ok"
        };

    private static AgentToolInvocationPrepareReleaseRequest ReleaseRequest(
        AgentToolBudgetReservation reservation,
        string auditId)
        => new()
        {
            AuditId = auditId,
            BudgetReservationId = reservation.ReservationId,
            ReasonCode = "no-dispatch"
        };

    private static async Task AssertThrowsInvalidOperationAsync(
        Func<ValueTask> action,
        string message)
    {
        Exception? captured = null;
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            captured = ex;
        }

        AgentToolPreDispatchContractAssertions.True(
            captured is not null,
            message);
    }

    /// <summary>
    /// Starts an operation and captures any exception (synchronous or
    /// asynchronous) so the true-race cases can run both participants
    /// concurrently without a faulted task escaping. The in-memory provider
    /// executes its ValueTask bodies synchronously, so the invocation itself
    /// may throw and must be wrapped.
    /// </summary>
    private static async Task<Exception?> CaptureAsync(Func<ValueTask> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task<(TResult? Result, Exception? Error)> CaptureResultAsync<TResult>(
        Func<ValueTask<TResult>> action)
    {
        try
        {
            return (await action().ConfigureAwait(false), null);
        }
        catch (Exception ex)
        {
            return (default, ex);
        }
    }
}
