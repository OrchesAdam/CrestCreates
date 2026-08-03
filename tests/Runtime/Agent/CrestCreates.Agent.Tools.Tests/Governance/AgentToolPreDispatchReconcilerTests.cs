using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
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

        // Observation should still exist but receipt is now terminal.
        var observation = await harness1.Store.ReadObservationAsync(identity);
        observation.Should().NotBeNull();
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

        // §7.8: Accepted + Released budget + Accepted checkpoint → Released (converge)
        new object[] { AgentToolInvocationPreDispatchState.Accepted, AgentToolBudgetReadStatus.Released, AgentToolGovernancePreDispatchReadStatus.Accepted, AgentToolPreDispatchReconciliationStatus.Released, true },

        // §7.10: Committed budget → Conflict
        new object[] { AgentToolInvocationPreDispatchState.Accepted, AgentToolBudgetReadStatus.Committed, AgentToolGovernancePreDispatchReadStatus.Accepted, AgentToolPreDispatchReconciliationStatus.Conflict, true },

        // §7.10: Indeterminate budget → StillPending
        new object[] { AgentToolInvocationPreDispatchState.Pending, AgentToolBudgetReadStatus.Indeterminate, AgentToolGovernancePreDispatchReadStatus.Missing, AgentToolPreDispatchReconciliationStatus.StillPending, false },

        // Authority unavailable → StillPending
        new object[] { AgentToolInvocationPreDispatchState.Pending, AgentToolBudgetReadStatus.Unknown, AgentToolGovernancePreDispatchReadStatus.Unknown, AgentToolPreDispatchReconciliationStatus.StillPending, false },

        // Pending + Reserved + Missing checkpoint → StillPending (can't prove dispatch false yet)
        new object[] { AgentToolInvocationPreDispatchState.Pending, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Missing, AgentToolPreDispatchReconciliationStatus.StillPending, false },

        // Ready + Reserved + Missing checkpoint → StillPending
        new object[] { AgentToolInvocationPreDispatchState.Ready, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Missing, AgentToolPreDispatchReconciliationStatus.StillPending, false },
    };

    private static AgentToolPreDispatchIdentity SampleIdentity(string attemptId)
        => new(SampleKey(), attemptId);

    private static AgentToolLogicalInvocationKey SampleKey()
        => new("tenant", "user", "agent", "execution", "invocation");

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
        private readonly AgentToolInvocationPreDispatchState _state;

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
            => ValueTask.FromResult(new AgentToolInvocationPreDispatchResult { State = _state, ReasonCode = "stub" });
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
    }

    private sealed class StubBudgetGate : IAgentToolBudgetGate
    {
        private readonly AgentToolBudgetReadStatus _status;

        public StubBudgetGate(AgentToolBudgetReadStatus status) => _status = status;

        public ValueTask<AgentToolBudgetReserveResult> ReserveAsync(AgentToolBudgetReserveRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolBudgetReservation> FinalizeAsync(AgentToolBudgetFinalizeRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new AgentToolBudgetReservationReadResult
            {
                Status = _status,
                Reservation = _status == AgentToolBudgetReadStatus.Reserved
                    ? new AgentToolBudgetReservation
                    {
                        ReservationId = "stub-reservation",
                        AttemptId = "attempt-1",
                        InvocationFingerprint = "fp-stub",
                        Category = "default",
                        CostUnits = 1,
                        MaxCallsPerExecution = 1,
                        State = AgentToolBudgetReservationState.Reserved
                    }
                    : null
            });
        }
    }

    private sealed class StubAuditor : IAgentToolGovernanceAuditor
    {
        private readonly AgentToolGovernancePreDispatchReadStatus _status;

        public StubAuditor(AgentToolGovernancePreDispatchReadStatus status) => _status = status;

        public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(AgentToolGovernancePreDispatchRecord record, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new AgentToolGovernancePreDispatchReadResult
            {
                Status = _status,
                Receipt = null,
                Checkpoint = null
            });
        }
        public ValueTask RecordDecisionAsync(AgentToolGovernanceDecisionRecord decision, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(AgentToolGovernanceFinalizationRecord record, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(string auditId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
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
                    return ValueTask.FromResult(false);
                _receipts[receipt.Identity.AttemptId] = receipt;
                return ValueTask.FromResult(true);
            }
        }
    }

    private static AgentToolLogicalInvocationKey StubKey => new("tenant", "user", "agent", "execution", "invocation");
}
