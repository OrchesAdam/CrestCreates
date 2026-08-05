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
}
