using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

/// <summary>
/// Settlement-executor tests (Issue #73 Slice 8.3). Verifies the claim-first
/// ownership fence: the Gate decides who owns the Attempt before any Budget or
/// Governance mutation, and every failure path leaves Budget and Governance
/// untouched. Uses dedicated failure-injectable stubs so the existing
/// reconciler harness stays untouched.
/// </summary>
public class AgentToolPreDispatchSettlementExecutorTests
{
    [Fact]
    public async Task Should_ClaimBeforeBudgetMutation()
    {
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.Accepted);

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(
                budgetAction: AgentToolPreDispatchBudgetAction.FinalizeReleased,
                governanceAction: AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.TerminalReceipt);
        result.ReasonCode.Should().Be("released_no_dispatch");

        // The claim must precede budget finalization which must precede Gate completion.
        harness.CallLog.Should().Equal(
            "gate.claim",
            "budget.finalize",
            "auditor.finalize",
            "gate.complete");
    }

    [Fact]
    public async Task ClaimFailure_Should_NotMutateBudgetOrGovernance()
    {
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.Accepted);
        harness.Gate.ClaimStatus = AgentToolPreDispatchReconciliationClaimStatus.NotClaimable;
        harness.Gate.ReReadStateAfterClaimFailure = AgentToolInvocationPreDispatchState.Accepted;

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(
                budgetAction: AgentToolPreDispatchBudgetAction.FinalizeReleased,
                governanceAction: AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.StillPending);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.Observation);
        result.ReasonCode.Should().Be("ownership_not_lost");

        harness.BudgetGate.FinalizeCallCount.Should().Be(0);
        harness.Auditor.FinalizeCallCount.Should().Be(0);
        harness.Gate.CompleteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ClaimFailure_DispatchStartedReRead_Should_ReturnPostDispatchUnknown()
    {
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.Accepted);
        harness.Gate.ClaimStatus = AgentToolPreDispatchReconciliationClaimStatus.NotClaimable;
        harness.Gate.ReReadStateAfterClaimFailure = AgentToolInvocationPreDispatchState.DispatchStarted;

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.TerminalReceipt);
        result.ReasonCode.Should().Be("dispatch_started");

        harness.BudgetGate.FinalizeCallCount.Should().Be(0);
        harness.Auditor.FinalizeCallCount.Should().Be(0);
        harness.Gate.CompleteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ClaimFailure_TerminalReRead_Should_ReturnReleasedTerminalRecovered()
    {
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.Accepted);
        harness.Gate.ClaimStatus = AgentToolPreDispatchReconciliationClaimStatus.NotClaimable;
        harness.Gate.ReReadStateAfterClaimFailure = AgentToolInvocationPreDispatchState.Released;

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.TerminalReceipt);
        result.ReasonCode.Should().Be("terminal_recovered");

        harness.BudgetGate.FinalizeCallCount.Should().Be(0);
        harness.Auditor.FinalizeCallCount.Should().Be(0);
        harness.Gate.CompleteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task BudgetFailure_Should_NotCompleteGate()
    {
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.Accepted);
        harness.BudgetGate.FinalizeResultState = AgentToolBudgetReservationState.Reserved; // did not reach Released

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(
                budgetAction: AgentToolPreDispatchBudgetAction.FinalizeReleased,
                governanceAction: AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Conflict);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.None);

        harness.Gate.CompleteCallCount.Should().Be(0);
        harness.Auditor.FinalizeCallCount.Should().Be(0);
    }

    [Fact]
    public async Task GovernanceFailure_Should_NotCompleteGate()
    {
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.Accepted);
        harness.Auditor.ThrowOnFinalize = true;

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(
                budgetAction: AgentToolPreDispatchBudgetAction.FinalizeReleased,
                governanceAction: AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.StillPending);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.Observation);
        result.ReasonCode.Should().Be("governance_finalization_unavailable");

        harness.Gate.CompleteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task GovernanceConflict_Should_NotCompleteGate()
    {
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.Accepted);
        harness.Auditor.ReturnMismatchedFinalization = true;

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(
                budgetAction: AgentToolPreDispatchBudgetAction.FinalizeReleased,
                governanceAction: AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Conflict);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.TerminalReceipt);
        result.ReasonCode.Should().Be("governance_finalization_conflict");

        harness.Gate.CompleteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task GovernanceEvidenceMissing_Should_ReturnConflict()
    {
        // Decision demands FinalizeReleasedNoDispatch but the checkpoint read carried
        // no record — the exact Released governance fact cannot be established, so
        // reconciliation cannot complete the Gate.
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.Accepted);

        var missingEvidenceSnapshot = new AgentToolPreDispatchAuthoritySnapshot
        {
            Gate = new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Accepted,
                Intent = StubIntent,
                BoundReservationId = "stub-reservation",
                AcceptedReceipt = StubReceipt,
                Revision = 1,
                ReasonCode = "stub"
            },
            Budget = new AgentToolBudgetReservationReadResult
            {
                Status = AgentToolBudgetReadStatus.Reserved,
                Reservation = StubReservation()
            },
            Checkpoint = new AgentToolGovernancePreDispatchReadResult
            {
                // Accepted status but no record — the exact Released fact is missing.
                Status = AgentToolGovernancePreDispatchReadStatus.Accepted,
                Receipt = null,
                Checkpoint = null
            }
        };

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(
                budgetAction: AgentToolPreDispatchBudgetAction.FinalizeReleased,
                governanceAction: AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch),
            missingEvidenceSnapshot,
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Conflict);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.TerminalReceipt);
        result.ReasonCode.Should().Be("governance_finalization_evidence_missing");

        harness.Gate.CompleteCallCount.Should().Be(0);
        harness.Auditor.FinalizeCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ReleasedDecision_Should_CompleteGateWithClaim()
    {
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.Accepted);

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(
                budgetAction: AgentToolPreDispatchBudgetAction.FinalizeReleased,
                governanceAction: AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.TerminalReceipt);
        harness.Gate.CompleteCallCount.Should().Be(1);
        harness.Gate.CompletedState.Should().Be(AgentToolInvocationPreDispatchState.Released);
        harness.BudgetGate.FinalizeCallCount.Should().Be(1);
        harness.Auditor.FinalizeCallCount.Should().Be(1);
        harness.Auditor.LastFinalization!.DispatchStarted.Should().BeFalse();
        harness.Auditor.LastFinalization.AttemptState.Should().Be(AgentToolGovernanceAttemptFinalState.Released);
        harness.Auditor.LastFinalization.BudgetReservation.State.Should().Be(AgentToolBudgetReservationState.Released);
    }

    [Fact]
    public async Task AbandonDecision_Should_CompleteGateAsAbandoned()
    {
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.Pending);

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            AbandonDecision(),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Missing),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);
        harness.Gate.CompletedState.Should().Be(AgentToolInvocationPreDispatchState.Abandoned);
        // Unrecorded attempt: no checkpoint, so no governance finalization.
        harness.Auditor.FinalizeCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RecoveredClaim_Should_Converge()
    {
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.ReconciliationPending);
        harness.Gate.CurrentState = AgentToolInvocationPreDispatchState.ReconciliationPending;
        harness.Gate.ExistingClaimToken = "rc-stub-existing";

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(
                budgetAction: AgentToolPreDispatchBudgetAction.FinalizeReleased,
                governanceAction: AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted,
                reconciliationClaimToken: "rc-stub-existing"),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);
        // A prior reconciler's durable claim is reused — no second claim attempt.
        harness.Gate.TryBeginCallCount.Should().Be(0);
        harness.Gate.CompleteCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ClaimFailure_ReconciliationPendingReRead_Should_RecoverClaim()
    {
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.Accepted);
        harness.Gate.ClaimStatus = AgentToolPreDispatchReconciliationClaimStatus.NotClaimable;
        harness.Gate.ReReadStateAfterClaimFailure = AgentToolInvocationPreDispatchState.ReconciliationPending;
        harness.Gate.ExistingClaimToken = "rc-stub-existing";

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(
                budgetAction: AgentToolPreDispatchBudgetAction.FinalizeReleased,
                governanceAction: AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted,
                reconciliationClaimToken: "rc-stub-existing"),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);
        harness.Gate.CompleteCallCount.Should().Be(1);
    }

    [Fact]
    public async Task BudgetActionNone_Should_SkipFinalize()
    {
        // A Released decision that only requires a Gate transition must not finalize
        // an already-Released budget again.
        var (executor, harness) = CreateExecutor(AgentToolInvocationPreDispatchState.Pending);

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(
                gateAction: AgentToolPreDispatchGateAction.ClaimAndAbandon,
                budgetAction: AgentToolPreDispatchBudgetAction.None,
                governanceAction: AgentToolPreDispatchGovernanceAction.None),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Released, AgentToolGovernancePreDispatchReadStatus.Missing),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);
        harness.BudgetGate.FinalizeCallCount.Should().Be(0);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────

    private static AgentToolPreDispatchRecoveryDecision ReleasedDecision(
        AgentToolPreDispatchGateAction gateAction = AgentToolPreDispatchGateAction.ClaimAndRelease,
        AgentToolPreDispatchBudgetAction budgetAction = AgentToolPreDispatchBudgetAction.FinalizeReleased,
        AgentToolPreDispatchGovernanceAction governanceAction = AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch)
        => new()
        {
            Disposition = AgentToolPreDispatchReconciliationStatus.Released,
            GateAction = gateAction,
            BudgetAction = budgetAction,
            GovernanceAction = governanceAction,
            RequiresOwnershipClaim = true,
            ReasonCode = "released_no_dispatch"
        };

    private static AgentToolPreDispatchRecoveryDecision AbandonDecision()
        => new()
        {
            Disposition = AgentToolPreDispatchReconciliationStatus.Released,
            GateAction = AgentToolPreDispatchGateAction.ClaimAndAbandon,
            BudgetAction = AgentToolPreDispatchBudgetAction.FinalizeReleased,
            GovernanceAction = AgentToolPreDispatchGovernanceAction.None,
            RequiresOwnershipClaim = true,
            ReasonCode = "budget_reserved_no_checkpoint"
        };

    private static AgentToolPreDispatchAuthoritySnapshot Snapshot(
        AgentToolInvocationPreDispatchState gateState,
        AgentToolBudgetReadStatus budgetStatus,
        AgentToolGovernancePreDispatchReadStatus checkpointStatus,
        string? reconciliationClaimToken = null)
    {
        var budgetReservation = budgetStatus switch
        {
            AgentToolBudgetReadStatus.Reserved => StubReservation(AgentToolBudgetReservationState.Reserved),
            AgentToolBudgetReadStatus.Released => StubReservation(AgentToolBudgetReservationState.Released),
            _ => null
        };

        return new AgentToolPreDispatchAuthoritySnapshot
        {
            Gate = new AgentToolInvocationPreDispatchResult
            {
                State = gateState,
                Intent = gateState is AgentToolInvocationPreDispatchState.Pending
                    or AgentToolInvocationPreDispatchState.Ready
                    or AgentToolInvocationPreDispatchState.Accepted
                    or AgentToolInvocationPreDispatchState.ReconciliationPending
                    ? StubIntent
                    : null,
                BoundReservationId = gateState is AgentToolInvocationPreDispatchState.Ready
                    or AgentToolInvocationPreDispatchState.Accepted
                    ? "stub-reservation"
                    : null,
                AcceptedReceipt = gateState == AgentToolInvocationPreDispatchState.Accepted
                    ? StubReceipt
                    : null,
                Revision = 1,
                ReconciliationClaimToken = gateState == AgentToolInvocationPreDispatchState.ReconciliationPending
                    ? reconciliationClaimToken
                    : null,
                ReconciliationClaimedState = gateState == AgentToolInvocationPreDispatchState.ReconciliationPending
                    ? AgentToolInvocationPreDispatchState.Accepted
                    : null,
                ReconciliationClaimedAt = gateState == AgentToolInvocationPreDispatchState.ReconciliationPending
                    ? DateTimeOffset.UtcNow
                    : null,
                ReasonCode = "stub"
            },
            Budget = new AgentToolBudgetReservationReadResult
            {
                Status = budgetStatus,
                Reservation = budgetReservation
            },
            Checkpoint = new AgentToolGovernancePreDispatchReadResult
            {
                Status = checkpointStatus,
                Receipt = checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted ? StubReceipt : null,
                Checkpoint = checkpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted ? StubCheckpoint : null
            }
        };
    }

    private static AgentToolPreDispatchIdentity SampleIdentity(string attemptId)
        => new(SampleKey(), attemptId);

    private static AgentToolLogicalInvocationKey SampleKey()
        => new("tenant", "user", "agent", "execution", "invocation");

    private static AgentToolInvocationLease StubLease => new()
    {
        LeaseId = "lease-1",
        AttemptId = "attempt-1",
        FencingToken = 1,
        AcquiredAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
        ExpiresAt = DateTimeOffset.Parse("2026-08-01T00:05:00Z")
    };

    private static AgentToolGovernanceAuditContext StubContext => new()
    {
        LogicalInvocationKey = SampleKey(),
        AttemptId = "attempt-1",
        InvocationFingerprint = "fp-stub",
        ArgumentsHash = "args-hash",
        ArgumentsEvaluated = true,
        CallOrigin = AgentToolCallOrigin.ExplicitRequest,
        AgentRolesHash = "roles-hash",
        ToolContract = new AgentToolContractIdentity("tool", 1, "tool-hash"),
        CapabilityContract = new AgentToolContractIdentity("capability", 1, "capability-hash"),
        Governance = new AgentToolEffectiveGovernance(
            AgentToolSelectionPolicy.ExplicitOnly,
            AgentToolSideEffectKind.ReadOnly,
            CapabilityRiskLevel.Low,
            AgentToolApprovalMode.None,
            new AgentToolBudgetRequirement
            {
                Category = "default",
                CostUnits = 1,
                MaxCallsPerExecution = 1
            },
            AgentToolAuditMode.Required)
    };

    private static AgentToolApprovalResult StubApproval => new()
    {
        Decision = AgentToolApprovalDecision.NotRequired,
        ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable
    };

    private static AgentToolBudgetReservation StubReservation(
        AgentToolBudgetReservationState state = AgentToolBudgetReservationState.Reserved)
        => new()
        {
            ReservationId = "stub-reservation",
            AttemptId = "attempt-1",
            InvocationFingerprint = "fp-stub",
            Category = "default",
            CostUnits = 1,
            MaxCallsPerExecution = 1,
            State = state
        };

    private static AgentToolGovernancePreDispatchReceipt StubReceipt => new()
    {
        Identity = SampleIdentity("attempt-1"),
        AuditId = "audit-1",
        AcceptedAt = DateTimeOffset.Parse("2026-08-01T00:01:00Z")
    };

    private static AgentToolInvocationPreDispatchIntentSnapshot StubIntent => new()
    {
        FrozenLease = StubLease,
        InvocationFingerprint = "fp-stub",
        Context = StubContext,
        Approval = StubApproval
    };

    private static AgentToolGovernancePreDispatchRecord StubCheckpoint => new()
    {
        Context = StubContext,
        Lease = StubLease,
        Approval = StubApproval,
        BudgetReservation = StubReservation()
    };

    private static (AgentToolPreDispatchSettlementExecutor executor, SettlementTestHarness harness) CreateExecutor(
        AgentToolInvocationPreDispatchState gateState)
    {
        var harness = new SettlementTestHarness(gateState);
        var executor = new AgentToolPreDispatchSettlementExecutor(
            harness.Gate,
            harness.BudgetGate,
            harness.Auditor);
        return (executor, harness);
    }

    private sealed class SettlementTestHarness
    {
        public StubGate Gate { get; }
        public StubBudgetGate BudgetGate { get; }
        public StubAuditor Auditor { get; }
        public List<string> CallLog { get; }

        public SettlementTestHarness(AgentToolInvocationPreDispatchState gateState)
        {
            Gate = new StubGate(gateState);
            BudgetGate = new StubBudgetGate();
            Auditor = new StubAuditor();
            CallLog = Gate.CallLog;
            BudgetGate.CallLog = CallLog;
            Auditor.CallLog = CallLog;
        }
    }

    private sealed class StubGate : IAgentToolInvocationGate
    {
        public List<string> CallLog { get; } = new();
        public AgentToolInvocationPreDispatchState CurrentState { get; set; }
        public AgentToolPreDispatchReconciliationClaimStatus ClaimStatus { get; set; } = AgentToolPreDispatchReconciliationClaimStatus.Claimed;
        public AgentToolInvocationPreDispatchState? ReReadStateAfterClaimFailure { get; set; }
        public string? ExistingClaimToken { get; set; }
        public AgentToolInvocationPreDispatchState CompletedState { get; private set; }
        public int TryBeginCallCount { get; private set; }
        public int CompleteCallCount { get; private set; }

        private long _revision = 1;
        private string? _claimToken;

        public StubGate(AgentToolInvocationPreDispatchState state) => CurrentState = state;

        public ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateAsync(
            AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            var state = ReReadStateAfterClaimFailure ?? CurrentState;
            // A read reflects the durable state — keep the stub's CurrentState in
            // sync so a subsequent Complete... sees the same transition.
            CurrentState = state;
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = state,
                Intent = state is AgentToolInvocationPreDispatchState.Pending
                    or AgentToolInvocationPreDispatchState.Ready
                    or AgentToolInvocationPreDispatchState.Accepted
                    or AgentToolInvocationPreDispatchState.ReconciliationPending
                    ? StubIntent
                    : null,
                BoundReservationId = state is AgentToolInvocationPreDispatchState.Ready
                    or AgentToolInvocationPreDispatchState.Accepted
                    ? "stub-reservation"
                    : null,
                AcceptedReceipt = state == AgentToolInvocationPreDispatchState.Accepted
                    ? StubReceipt
                    : null,
                Revision = _revision,
                ReconciliationClaimToken = state == AgentToolInvocationPreDispatchState.ReconciliationPending
                    ? (ExistingClaimToken ?? _claimToken)
                    : null,
                ReconciliationClaimedState = state == AgentToolInvocationPreDispatchState.ReconciliationPending
                    ? AgentToolInvocationPreDispatchState.Accepted
                    : null,
                ReconciliationClaimedAt = state == AgentToolInvocationPreDispatchState.ReconciliationPending
                    ? DateTimeOffset.UtcNow
                    : null,
                ReasonCode = state is AgentToolInvocationPreDispatchState.Released
                    or AgentToolInvocationPreDispatchState.Abandoned
                    ? "terminal_recovered"
                    : "stub"
            });
        }

        public ValueTask<AgentToolPreDispatchReconciliationClaimResult> TryBeginPreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaimRequest request, CancellationToken cancellationToken = default)
        {
            TryBeginCallCount++;
            if (ClaimStatus != AgentToolPreDispatchReconciliationClaimStatus.Claimed)
            {
                return ValueTask.FromResult(new AgentToolPreDispatchReconciliationClaimResult
                {
                    Status = ClaimStatus,
                    ReasonCode = ClaimStatus == AgentToolPreDispatchReconciliationClaimStatus.RevisionConflict
                        ? "revision_conflict"
                        : "state_not_claimable"
                });
            }

            CallLog.Add("gate.claim");
            var claimToken = $"rc-stub-{Guid.NewGuid():N}";
            _claimToken = claimToken;
            _revision++;
            CurrentState = AgentToolInvocationPreDispatchState.ReconciliationPending;
            return ValueTask.FromResult(new AgentToolPreDispatchReconciliationClaimResult
            {
                Status = AgentToolPreDispatchReconciliationClaimStatus.Claimed,
                Claim = new AgentToolPreDispatchReconciliationClaim
                {
                    Identity = request.Identity,
                    Revision = _revision,
                    ClaimToken = claimToken,
                    ClaimedAt = DateTimeOffset.UtcNow,
                    ClaimedState = AgentToolInvocationPreDispatchState.Accepted,
                    Indeterminate = false,
                    BoundReservationId = "stub-reservation",
                    AcceptedReceipt = StubReceipt
                }
            });
        }

        public ValueTask<AgentToolInvocationPreDispatchResult> CompletePreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaim claim,
            AgentToolPreDispatchReconciliationCompletionKind kind,
            string reasonCode,
            CancellationToken cancellationToken = default)
        {
            CompleteCallCount++;
            CallLog.Add("gate.complete");
            var activeClaimToken = ExistingClaimToken ?? _claimToken;
            if (!string.Equals(activeClaimToken, claim.ClaimToken, StringComparison.Ordinal)
                || CurrentState != AgentToolInvocationPreDispatchState.ReconciliationPending)
            {
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = "reconciliation_completion_conflict"
                });
            }

            CompletedState = kind == AgentToolPreDispatchReconciliationCompletionKind.Abandoned
                ? AgentToolInvocationPreDispatchState.Abandoned
                : AgentToolInvocationPreDispatchState.Released;
            CurrentState = CompletedState;
            _revision++;
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = CompletedState,
                ReasonCode = reasonCode
            });
        }

        public ValueTask<AgentToolInvocationAcquireResult> AcquireAsync(AgentToolInvocationAcquireRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask AbandonUnrecordedLeaseAsync(AgentToolInvocationLease lease, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationLease> RenewAsync(AgentToolInvocationLease lease, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationPreDispatchResult> PreparePreDispatchIntentAsync(AgentToolInvocationLease lease, AgentToolInvocationPreparePreDispatchIntentRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationPreDispatchResult> BindPreDispatchReservationAsync(AgentToolInvocationLease lease, AgentToolInvocationBindReservationRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedPreDispatchAsync(AgentToolInvocationLease lease, AgentToolInvocationBindPreDispatchRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialAsync(AgentToolInvocationLease lease, AgentToolInvocationPublishDenialRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<bool> TryMarkDispatchStartedAsync(AgentToolInvocationLease lease, AgentToolGovernancePreDispatchReceipt receipt, string reservationId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask PrepareCompletionAsync(AgentToolInvocationLease lease, AgentToolInvocationPrepareCompletionRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationCompletionResult> PublishCompletionAsync(AgentToolInvocationLease lease, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateAsync(AgentToolInvocationLease lease, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask PrepareReleaseAsync(AgentToolInvocationLease lease, AgentToolInvocationPrepareReleaseRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationReleaseResult> PublishReleaseAsync(AgentToolInvocationLease lease, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateAsync(AgentToolInvocationLease lease, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask MarkIndeterminateAsync(AgentToolInvocationLease lease, string reasonCode, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationPreDispatchResult> ReleaseByIdentityAsync(AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationPreDispatchResult> AbandonByIdentityAsync(AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubBudgetGate : IAgentToolBudgetGate
    {
        public List<string> CallLog { get; set; } = new();
        public AgentToolBudgetReservationState FinalizeResultState { get; set; } = AgentToolBudgetReservationState.Released;
        public int FinalizeCallCount { get; private set; }

        public ValueTask<AgentToolBudgetReservation> FinalizeAsync(AgentToolBudgetFinalizeRequest request, CancellationToken cancellationToken = default)
        {
            FinalizeCallCount++;
            CallLog.Add("budget.finalize");
            return ValueTask.FromResult(new AgentToolBudgetReservation
            {
                ReservationId = request.ReservationId,
                AttemptId = request.AttemptId,
                InvocationFingerprint = request.InvocationFingerprint,
                Category = "default",
                CostUnits = 1,
                MaxCallsPerExecution = 1,
                State = FinalizeResultState
            });
        }

        public ValueTask<AgentToolBudgetReserveResult> ReserveAsync(AgentToolBudgetReserveRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubAuditor : IAgentToolGovernanceAuditor
    {
        public List<string> CallLog { get; set; } = new();
        public bool ThrowOnFinalize { get; set; }
        public bool ReturnMismatchedFinalization { get; set; }
        public int FinalizeCallCount { get; private set; }
        public AgentToolGovernanceFinalizationRecord? LastFinalization { get; private set; }

        public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record, CancellationToken cancellationToken = default)
        {
            FinalizeCallCount++;
            CallLog.Add("auditor.finalize");
            if (ThrowOnFinalize)
                throw new InvalidOperationException("audit sink unavailable");

            LastFinalization = record;
            if (ReturnMismatchedFinalization)
            {
                return ValueTask.FromResult(new AgentToolGovernanceFinalizationResult
                {
                    Status = AgentToolGovernanceFinalizationStatus.Finalized,
                    Record = record with { DispatchStarted = true }
                });
            }

            return ValueTask.FromResult(new AgentToolGovernanceFinalizationResult
            {
                Status = AgentToolGovernanceFinalizationStatus.Finalized,
                Record = record
            });
        }

        public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(AgentToolGovernancePreDispatchRecord record, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask RecordDecisionAsync(AgentToolGovernanceDecisionRecord decision, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(string auditId, string? tenantId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
