using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

/// <summary>
/// Table-driven reconciliation matrix (§7.7, §7.10) covering H06–H08,
/// B08/B12/B18, F14–F17/F27/F28/F30, and C03.
/// </summary>
public class AgentToolPreDispatchReconcilerTests
{
    [Theory]
    [MemberData(nameof(ReconciliationMatrix))]
    public async Task Reconcile_Produces_Correct_Status(
        AgentToolInvocationPreDispatchState gateState,
        AgentToolBudgetReadStatus budgetStatus,
        AgentToolGovernancePreDispatchReadStatus checkpointStatus,
        AgentToolPreDispatchReconciliationStatus expectedStatus,
        bool expectTerminalReceipt)
    {
        var (reconciler, harness) = CreateReconciler(gateState, budgetStatus, checkpointStatus);
        var identity = SampleIdentity("attempt-1");

        var result = await reconciler.ReconcileAsync(identity);

        result.Status.Should().Be(expectedStatus);

        if (expectTerminalReceipt)
        {
            result.Receipt.Should().NotBeNull();
            result.Observation.Should().BeNull();
        }
        else
        {
            result.Observation.Should().NotBeNull();
            result.Receipt.Should().BeNull();
        }
    }

    [Fact]
    public async Task Reconcile_AlreadyTerminal_ReturnsAlreadyReleased_FromExistingReceipt()
    {
        var identity = SampleIdentity("attempt-1");
        var (reconciler, harness) = CreateReconciler(
            AgentToolInvocationPreDispatchState.Released,
            AgentToolBudgetReadStatus.Released,
            AgentToolGovernancePreDispatchReadStatus.Accepted);

        // Pre-insert a terminal receipt.
        await harness.Store.TryInsertReceiptAsync(new AgentToolPreDispatchReconciliationReceipt
        {
            Identity = identity,
            Status = AgentToolPreDispatchReconciliationStatus.Released,
            ReasonCode = "released_no_dispatch",
            TerminalAt = DateTimeOffset.UtcNow,
            IntegrityValue = "test"
        });

        var result = await reconciler.ReconcileAsync(identity);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.AlreadyReleased);
        result.Receipt.Should().NotBeNull();
    }

    [Fact]
    public async Task Reconcile_TerminalGate_AfterReceiptWriteLoss_Should_CreateReceipt()
    {
        var identity = SampleIdentity("attempt-1");
        var (reconciler, harness) = CreateReconciler(
            AgentToolInvocationPreDispatchState.Released,
            AgentToolBudgetReadStatus.Released,
            AgentToolGovernancePreDispatchReadStatus.Accepted);

        var result = await reconciler.ReconcileAsync(identity);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);
        result.Receipt.Should().NotBeNull();
        (await harness.Store.ReadReceiptAsync(identity)).Should().BeEquivalentTo(result.Receipt);
        harness.BudgetGate.FinalizeCallCount.Should().Be(0);
        harness.Gate.ReleaseCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Reconcile_RepeatedPostDispatchUnknown_Should_Not_Project_AlreadyReleased()
    {
        var identity = SampleIdentity("attempt-1");
        var (reconciler, _) = CreateReconciler(
            AgentToolInvocationPreDispatchState.DispatchStarted,
            AgentToolBudgetReadStatus.Committed,
            AgentToolGovernancePreDispatchReadStatus.Accepted);

        (await reconciler.ReconcileAsync(identity)).Status
            .Should().Be(AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown);
        (await reconciler.ReconcileAsync(identity)).Status
            .Should().Be(AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown);
    }

    [Fact]
    public async Task Reconcile_StillPending_Persists_Mutable_Observation()
    {
        var identity = SampleIdentity("attempt-1");
        var (reconciler, harness) = CreateReconciler(
            AgentToolInvocationPreDispatchState.Pending,
            AgentToolBudgetReadStatus.Unknown,
            AgentToolGovernancePreDispatchReadStatus.Unknown);

        var result = await reconciler.ReconcileAsync(identity);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.StillPending);
        result.Observation.Should().NotBeNull();
        result.Receipt.Should().BeNull();

        var stored = await harness.Store.ReadObservationAsync(identity);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.StillPending);
        stored.Revision.Should().Be(1);
    }

    [Fact]
    public async Task Reconcile_StillPending_Can_Advance_To_Released()
    {
        var identity = SampleIdentity("attempt-1");

        // First reconcile: authority unavailable → StillPending
        var (reconciler1, harness1) = CreateReconciler(
            AgentToolInvocationPreDispatchState.Pending,
            AgentToolBudgetReadStatus.Unknown,
            AgentToolGovernancePreDispatchReadStatus.Unknown);

        var result1 = await reconciler1.ReconcileAsync(identity);
        result1.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.StillPending);

        // Second reconcile: authorities now available → Released (abandoned)
        var (reconciler2, _) = CreateReconciler(
            AgentToolInvocationPreDispatchState.Pending,
            AgentToolBudgetReadStatus.Missing,
            AgentToolGovernancePreDispatchReadStatus.Missing,
            harness1.Store);

        var result2 = await reconciler2.ReconcileAsync(identity);
        result2.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);
        result2.Receipt.Should().NotBeNull();

        // Terminal receipt atomically supersedes retryable observation.
        var observation = await harness1.Store.ReadObservationAsync(identity);
        observation.Should().BeNull();
        var receipt = await harness1.Store.ReadReceiptAsync(identity);
        receipt.Should().NotBeNull();
    }

    [Fact]
    public async Task Reconcile_Does_Not_Call_Dispatcher()
    {
        var (reconciler, harness) = CreateReconciler(
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Accepted);

        await reconciler.ReconcileAsync(SampleIdentity("attempt-1"));

        harness.DispatchCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Reconcile_Does_Not_Evaluate_Approval()
    {
        var (reconciler, harness) = CreateReconciler(
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Accepted);

        await reconciler.ReconcileAsync(SampleIdentity("attempt-1"));

        harness.ApprovalCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Reconcile_Repeated_Released_Returns_AlreadyReleased()
    {
        var identity = SampleIdentity("attempt-1");
        var (reconciler, harness) = CreateReconciler(
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Accepted);

        // First reconcile → Released
        var result1 = await reconciler.ReconcileAsync(identity);
        result1.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);

        // Second reconcile → AlreadyReleased
        var result2 = await reconciler.ReconcileAsync(identity);
        result2.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.AlreadyReleased);
        result2.Receipt.Should().NotBeNull();
        harness.Auditor.FinalizeCallCount.Should().Be(1);
        harness.Auditor.LastFinalization!.AttemptState
            .Should().Be(AgentToolGovernanceAttemptFinalState.Released);
        harness.Auditor.LastFinalization.DispatchStarted.Should().BeFalse();
        harness.Auditor.LastFinalization.BudgetReservation.State
            .Should().Be(AgentToolBudgetReservationState.Released);
    }

    [Fact]
    public async Task Reconcile_Terminal_Receipt_Is_Immutable()
    {
        var identity = SampleIdentity("attempt-1");
        var (reconciler, harness) = CreateReconciler(
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Accepted);

        var result1 = await reconciler.ReconcileAsync(identity);
        var firstReceipt = result1.Receipt!;
        var firstIntegrity = firstReceipt.IntegrityValue;

        // Second reconcile should not create a new receipt.
        var result2 = await reconciler.ReconcileAsync(identity);
        result2.Receipt!.IntegrityValue.Should().Be(firstIntegrity);
    }

    [Fact]
    public async Task Reconcile_ChangedFrozenApproval_Should_Conflict_BeforeBudgetOrGateTransition()
    {
        var (reconciler, harness) = CreateReconciler(
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Accepted);
        harness.Auditor.CheckpointOverride = StubCheckpoint with
        {
            Approval = StubApproval with { ReasonCode = "changed" }
        };

        var result = await reconciler.ReconcileAsync(SampleIdentity("attempt-1"));

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Conflict);
        harness.Gate.ReleaseCallCount.Should().Be(0);
        harness.BudgetGate.FinalizeCallCount.Should().Be(0);
    }

    public static IEnumerable<object[]> ReconciliationMatrix => new[]
    {
        // §7.7: Pending + Budget Missing + Checkpoint Missing → Released (abandoned)
        new object[] { AgentToolInvocationPreDispatchState.Pending, AgentToolBudgetReadStatus.Missing, AgentToolGovernancePreDispatchReadStatus.Missing, AgentToolPreDispatchReconciliationStatus.Released, true },

        // §7.10: Ready + Budget Missing → Conflict
        new object[] { AgentToolInvocationPreDispatchState.Ready, AgentToolBudgetReadStatus.Missing, AgentToolGovernancePreDispatchReadStatus.Missing, AgentToolPreDispatchReconciliationStatus.Conflict, true },

        // §7.10: Accepted + Budget Missing → Conflict
        new object[] { AgentToolInvocationPreDispatchState.Accepted, AgentToolBudgetReadStatus.Missing, AgentToolGovernancePreDispatchReadStatus.Accepted, AgentToolPreDispatchReconciliationStatus.Conflict, true },

        // §7.9: Accepted + Reserved + Accepted checkpoint → Released
        new object[] { AgentToolInvocationPreDispatchState.Accepted, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted, AgentToolPreDispatchReconciliationStatus.Released, true },

        // CW07/CW08/CW09: Ready + Reserved + Accepted checkpoint → validate + finalize + release.
        new object[] { AgentToolInvocationPreDispatchState.Ready, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted, AgentToolPreDispatchReconciliationStatus.Released, true },

        // Crash between budget finalize and gate transition (recorded attempt) → converge.
        new object[] { AgentToolInvocationPreDispatchState.Ready, AgentToolBudgetReadStatus.Released, AgentToolGovernancePreDispatchReadStatus.Accepted, AgentToolPreDispatchReconciliationStatus.Released, true },

        // Crash between budget finalize and gate transition (unrecorded attempt) → abandon.
        new object[] { AgentToolInvocationPreDispatchState.Pending, AgentToolBudgetReadStatus.Released, AgentToolGovernancePreDispatchReadStatus.Missing, AgentToolPreDispatchReconciliationStatus.Released, true },

        // §7.8: Accepted + Released budget + Accepted checkpoint → Released (converge)
        new object[] { AgentToolInvocationPreDispatchState.Accepted, AgentToolBudgetReadStatus.Released, AgentToolGovernancePreDispatchReadStatus.Accepted, AgentToolPreDispatchReconciliationStatus.Released, true },

        // §7.10: Committed budget → Conflict
        new object[] { AgentToolInvocationPreDispatchState.Accepted, AgentToolBudgetReadStatus.Committed, AgentToolGovernancePreDispatchReadStatus.Accepted, AgentToolPreDispatchReconciliationStatus.Conflict, true },

        // §7.10: Indeterminate budget → StillPending
        new object[] { AgentToolInvocationPreDispatchState.Pending, AgentToolBudgetReadStatus.Indeterminate, AgentToolGovernancePreDispatchReadStatus.Missing, AgentToolPreDispatchReconciliationStatus.StillPending, false },

        // Authority unavailable → StillPending
        new object[] { AgentToolInvocationPreDispatchState.Pending, AgentToolBudgetReadStatus.Unknown, AgentToolGovernancePreDispatchReadStatus.Unknown, AgentToolPreDispatchReconciliationStatus.StillPending, false },

        // CW04/CW05: Pending + Reserved + Missing checkpoint → reservation released, attempt abandoned.
        new object[] { AgentToolInvocationPreDispatchState.Pending, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Missing, AgentToolPreDispatchReconciliationStatus.Released, true },

        // CW04/CW05: Ready + Reserved + Missing checkpoint → reservation released, attempt abandoned.
        new object[] { AgentToolInvocationPreDispatchState.Ready, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Missing, AgentToolPreDispatchReconciliationStatus.Released, true },
    };

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

    private static (DefaultAgentToolPreDispatchReconciler reconciler, ReconcilerTestHarness harness) CreateReconciler(
        AgentToolInvocationPreDispatchState gateState,
        AgentToolBudgetReadStatus budgetStatus,
        AgentToolGovernancePreDispatchReadStatus checkpointStatus,
        IAgentToolPreDispatchReconciliationStore? store = null)
    {
        var harness = new ReconcilerTestHarness(gateState, budgetStatus, checkpointStatus, store);
        var reconciler = new DefaultAgentToolPreDispatchReconciler(
            harness.Gate,
            harness.BudgetGate,
            harness.Auditor,
            harness.Store,
            TimeProvider.System,
            null);
        return (reconciler, harness);
    }

    private sealed class ReconcilerTestHarness
    {
        public StubInvocationGate Gate { get; }
        public StubBudgetGate BudgetGate { get; }
        public StubAuditor Auditor { get; }
        public InMemoryReconciliationStore Store { get; }
        public int DispatchCallCount { get; set; }
        public int ApprovalCallCount { get; set; }

        public ReconcilerTestHarness(
            AgentToolInvocationPreDispatchState gateState,
            AgentToolBudgetReadStatus budgetStatus,
            AgentToolGovernancePreDispatchReadStatus checkpointStatus,
            IAgentToolPreDispatchReconciliationStore? store = null)
        {
            Gate = new StubInvocationGate(gateState);
            BudgetGate = new StubBudgetGate(budgetStatus);
            Auditor = new StubAuditor(checkpointStatus);
            Store = store as InMemoryReconciliationStore ?? new InMemoryReconciliationStore();
        }
    }

    private sealed class StubInvocationGate : IAgentToolInvocationGate
    {
        private AgentToolInvocationPreDispatchState _state;
        private long _revision = 1;
        private string? _claimToken;
        private AgentToolInvocationPreDispatchState? _claimedState;
        public int ReleaseCallCount { get; private set; }

        public StubInvocationGate(AgentToolInvocationPreDispatchState state) => _state = state;

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
        public ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = _state,
                Intent = _state is AgentToolInvocationPreDispatchState.Pending
                    or AgentToolInvocationPreDispatchState.Ready
                    or AgentToolInvocationPreDispatchState.Accepted
                    ? StubIntent
                    : null,
                BoundReservationId = _state is AgentToolInvocationPreDispatchState.Ready
                    or AgentToolInvocationPreDispatchState.Accepted
                    ? "stub-reservation"
                    : null,
                AcceptedReceipt = _state == AgentToolInvocationPreDispatchState.Accepted
                    ? StubReceipt
                    : null,
                Revision = _revision,
                ReconciliationClaimToken = _claimToken,
                ReconciliationClaimedState = _claimedState,
                ReasonCode = "stub"
            });
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
        public ValueTask<AgentToolInvocationPreDispatchResult> ReleaseByIdentityAsync(
            AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
        {
            ReleaseCallCount++;
            _state = AgentToolInvocationPreDispatchState.Released;
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult { State = _state, ReasonCode = reasonCode });
        }

        public ValueTask<AgentToolInvocationPreDispatchResult> AbandonByIdentityAsync(
            AgentToolPreDispatchIdentity identity, string reasonCode, CancellationToken cancellationToken = default)
        {
            _state = AgentToolInvocationPreDispatchState.Abandoned;
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult { State = _state, ReasonCode = reasonCode });
        }

        public ValueTask<AgentToolPreDispatchReconciliationClaimResult> TryBeginPreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaimRequest request, CancellationToken cancellationToken = default)
        {
            // The stub models an ownership-lost environment: any Pending/Ready/Accepted
            // Attempt with a matching revision is claimable.
            if (_state is not (AgentToolInvocationPreDispatchState.Pending
                or AgentToolInvocationPreDispatchState.Ready
                or AgentToolInvocationPreDispatchState.Accepted))
                return ValueTask.FromResult(new AgentToolPreDispatchReconciliationClaimResult
                {
                    Status = AgentToolPreDispatchReconciliationClaimStatus.NotClaimable,
                    ReasonCode = "state_not_claimable"
                });
            if (_revision != request.ExpectedRevision)
                return ValueTask.FromResult(new AgentToolPreDispatchReconciliationClaimResult
                {
                    Status = AgentToolPreDispatchReconciliationClaimStatus.RevisionConflict,
                    ReasonCode = "revision_conflict"
                });

            var claimToken = $"rc-stub-{Guid.NewGuid():N}";
            _claimToken = claimToken;
            _claimedState = _state;
            _revision++;
            _state = AgentToolInvocationPreDispatchState.ReconciliationPending;
            return ValueTask.FromResult(new AgentToolPreDispatchReconciliationClaimResult
            {
                Status = AgentToolPreDispatchReconciliationClaimStatus.Claimed,
                Claim = new AgentToolPreDispatchReconciliationClaim
                {
                    Identity = request.Identity,
                    Revision = _revision,
                    ClaimToken = claimToken,
                    ClaimedAt = DateTimeOffset.UtcNow,
                    ClaimedState = _claimedState.Value,
                    Indeterminate = false,
                    BoundReservationId = _claimedState is AgentToolInvocationPreDispatchState.Ready
                        or AgentToolInvocationPreDispatchState.Accepted
                        ? "stub-reservation"
                        : null,
                    AcceptedReceipt = _claimedState == AgentToolInvocationPreDispatchState.Accepted
                        ? StubReceipt
                        : null
                }
            });
        }

        public ValueTask<AgentToolInvocationPreDispatchResult> CompletePreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaim claim,
            AgentToolPreDispatchReconciliationCompletionKind kind,
            string reasonCode,
            CancellationToken cancellationToken = default)
        {
            if (_state != AgentToolInvocationPreDispatchState.ReconciliationPending
                || !string.Equals(_claimToken, claim.ClaimToken, StringComparison.Ordinal))
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Unknown,
                    ReasonCode = "reconciliation_completion_conflict"
                });

            _state = kind == AgentToolPreDispatchReconciliationCompletionKind.Abandoned
                ? AgentToolInvocationPreDispatchState.Abandoned
                : AgentToolInvocationPreDispatchState.Released;
            if (kind == AgentToolPreDispatchReconciliationCompletionKind.Released)
                ReleaseCallCount++;
            _revision++;
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = _state,
                ReasonCode = reasonCode
            });
        }
    }

    private sealed class StubBudgetGate : IAgentToolBudgetGate
    {
        private readonly AgentToolBudgetReadStatus _status;
        public int FinalizeCallCount { get; private set; }

        public StubBudgetGate(AgentToolBudgetReadStatus status) => _status = status;

        public ValueTask<AgentToolBudgetReserveResult> ReserveAsync(AgentToolBudgetReserveRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolBudgetReservation> FinalizeAsync(AgentToolBudgetFinalizeRequest request, CancellationToken cancellationToken = default)
        {
            FinalizeCallCount++;
            return ValueTask.FromResult(new AgentToolBudgetReservation
            {
                ReservationId = request.ReservationId,
                AttemptId = request.AttemptId,
                InvocationFingerprint = request.InvocationFingerprint,
                Category = "default",
                CostUnits = 1,
                MaxCallsPerExecution = 1,
                State = request.RequestedState
            });
        }
        public ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            var state = _status switch
            {
                AgentToolBudgetReadStatus.Reserved => AgentToolBudgetReservationState.Reserved,
                AgentToolBudgetReadStatus.Released => AgentToolBudgetReservationState.Released,
                AgentToolBudgetReadStatus.Committed => AgentToolBudgetReservationState.Committed,
                AgentToolBudgetReadStatus.Indeterminate => AgentToolBudgetReservationState.Indeterminate,
                _ => (AgentToolBudgetReservationState?)null
            };
            return ValueTask.FromResult(new AgentToolBudgetReservationReadResult
            {
                Status = _status,
                Reservation = state.HasValue ? StubReservation(state.Value) : null
            });
        }
    }

    private sealed class StubAuditor : IAgentToolGovernanceAuditor
    {
        private readonly AgentToolGovernancePreDispatchReadStatus _status;
        public AgentToolGovernancePreDispatchRecord? CheckpointOverride { get; set; }
        public int FinalizeCallCount { get; private set; }
        public AgentToolGovernanceFinalizationRecord? LastFinalization { get; private set; }

        public StubAuditor(AgentToolGovernancePreDispatchReadStatus status) => _status = status;

        public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(AgentToolGovernancePreDispatchRecord record, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new AgentToolGovernancePreDispatchReadResult
            {
                Status = _status,
                Receipt = _status == AgentToolGovernancePreDispatchReadStatus.Accepted
                    ? StubReceipt
                    : null,
                Checkpoint = _status == AgentToolGovernancePreDispatchReadStatus.Accepted
                    ? CheckpointOverride ?? StubCheckpoint
                    : null
            });
        }
        public ValueTask RecordDecisionAsync(AgentToolGovernanceDecisionRecord decision, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(AgentToolGovernanceFinalizationRecord record, CancellationToken cancellationToken = default)
        {
            FinalizeCallCount++;
            LastFinalization = record;
            return ValueTask.FromResult(new AgentToolGovernanceFinalizationResult
            {
                Status = AgentToolGovernanceFinalizationStatus.Finalized,
                Record = record
            });
        }
        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(string auditId, string? tenantId = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentToolGovernanceFinalizationResult
            {
                Status = AgentToolGovernanceFinalizationStatus.NotFinalized
            });
    }

    private sealed class InMemoryReconciliationStore : IAgentToolPreDispatchReconciliationStore
    {
        private readonly Dictionary<string, AgentToolPreDispatchReconciliationObservation> _observations = new();
        private readonly Dictionary<string, AgentToolPreDispatchReconciliationReceipt> _receipts = new();
        private readonly object _lock = new();

        public ValueTask<AgentToolPreDispatchReconciliationObservation?> ReadObservationAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _observations.TryGetValue(identity.AttemptId, out var obs);
                return ValueTask.FromResult(obs);
            }
        }

        public ValueTask<bool> TryUpsertObservationAsync(AgentToolPreDispatchReconciliationObservation observation, long expectedRevision, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_receipts.ContainsKey(observation.Identity.AttemptId))
                    return ValueTask.FromResult(false);

                if (_observations.TryGetValue(observation.Identity.AttemptId, out var existing))
                {
                    if (existing.Revision != expectedRevision)
                        return ValueTask.FromResult(false);
                }
                _observations[observation.Identity.AttemptId] = observation;
                return ValueTask.FromResult(true);
            }
        }

        public ValueTask<AgentToolPreDispatchReconciliationReceipt?> ReadReceiptAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _receipts.TryGetValue(identity.AttemptId, out var receipt);
                return ValueTask.FromResult(receipt);
            }
        }

        public ValueTask<bool> TryInsertReceiptAsync(AgentToolPreDispatchReconciliationReceipt receipt, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_receipts.ContainsKey(receipt.Identity.AttemptId))
                {
                    _observations.Remove(receipt.Identity.AttemptId);
                    return ValueTask.FromResult(false);
                }
                _receipts[receipt.Identity.AttemptId] = receipt;
                _observations.Remove(receipt.Identity.AttemptId);
                return ValueTask.FromResult(true);
            }
        }
    }

    private static AgentToolLogicalInvocationKey StubKey => new("tenant", "user", "agent", "execution", "invocation");
}
