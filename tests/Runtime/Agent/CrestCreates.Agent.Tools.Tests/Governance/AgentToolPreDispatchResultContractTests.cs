using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Agent.Tools.Persistence.Testing.Cases;
using CrestCreates.Agent.Tools.Persistence.Testing.Drivers;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

/// <summary>
/// Issue #73 Definition of Done: "one place persists results". Every durable
/// reconciliation outcome must have exactly one durable form — a terminal
/// disposition always carries an immutable receipt, a StillPending disposition
/// always carries a mutable observation. A bare terminal status is never a legal
/// protocol outcome.
/// </summary>
public class AgentToolPreDispatchResultContractTests
{
    [Theory]
    [InlineData(AgentToolPreDispatchReconciliationStatus.Released)]
    [InlineData(AgentToolPreDispatchReconciliationStatus.Conflict)]
    [InlineData(AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown)]
    public async Task Every_Terminal_Result_Should_Carry_Receipt(
        AgentToolPreDispatchReconciliationStatus status)
    {
        var (resultWriter, store) = CreateWriterHarness();
        var identity = SampleIdentity("attempt-1");

        var result = await resultWriter.WriteTerminalAsync(identity, status, $"reason-{status}", default);

        result.Status.Should().Be(status);
        result.Receipt.Should().NotBeNull("a terminal disposition must persist an immutable receipt.");
        result.Receipt!.Status.Should().Be(status);
        result.Receipt.Identity.Should().Be(identity);
        result.Receipt.ReasonCode.Should().Be($"reason-{status}");
        result.Receipt.TerminalAt.Should().NotBe(default);
        result.Receipt.IntegrityValue.Should().NotBeNullOrEmpty();
        result.Observation.Should().BeNull("a terminal receipt and a retry observation never coexist.");

        var persisted = await store.ReadReceiptAsync(identity);
        persisted.Should().NotBeNull("the receipt must actually be durable, not just returned.");
        persisted!.Status.Should().Be(status);
    }

    [Fact]
    public async Task Every_StillPending_Result_Should_Carry_Observation()
    {
        var (resultWriter, store) = CreateWriterHarness();
        var identity = SampleIdentity("attempt-1");

        var result = await resultWriter.WriteObservationAsync(
            identity, AgentToolPreDispatchReconciliationStatus.StillPending, "authority_unavailable", default);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.StillPending);
        result.Observation.Should().NotBeNull("a retryable disposition must persist a mutable observation.");
        result.Observation!.Identity.Should().Be(identity);
        result.Observation.ReasonCode.Should().Be("authority_unavailable");
        result.Observation.Revision.Should().Be(1);
        result.Receipt.Should().BeNull("an observation is not a terminal receipt.");

        var persisted = await store.ReadObservationAsync(identity);
        persisted.Should().NotBeNull("the observation must actually be durable, not just returned.");
    }

    [Fact]
    public async Task Repeated_Observation_Should_AdvanceRevision()
    {
        var (resultWriter, _) = CreateWriterHarness();
        var identity = SampleIdentity("attempt-1");

        var first = await resultWriter.WriteObservationAsync(identity, AgentToolPreDispatchReconciliationStatus.StillPending, "retry-1", default);
        var second = await resultWriter.WriteObservationAsync(identity, AgentToolPreDispatchReconciliationStatus.StillPending, "retry-2", default);

        first.Observation!.Revision.Should().Be(1);
        second.Observation!.Revision.Should().Be(2);
        second.Observation.ReasonCode.Should().Be("retry-2");
    }

    [Fact]
    public async Task Repeated_Terminal_Write_Should_ReplaySameReceipt()
    {
        var (resultWriter, _) = CreateWriterHarness();
        var identity = SampleIdentity("attempt-1");

        var first = await resultWriter.WriteTerminalAsync(identity, AgentToolPreDispatchReconciliationStatus.Conflict, "conflict-1", default);
        var second = await resultWriter.WriteTerminalAsync(identity, AgentToolPreDispatchReconciliationStatus.Released, "released-2", default);

        // First write wins; a later reconciler cannot overwrite the terminal fact.
        second.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Conflict);
        second.Receipt.Should().NotBeNull();
        second.Receipt!.IntegrityValue.Should().Be(first.Receipt!.IntegrityValue);
        second.Receipt.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Conflict);
    }

    [Fact]
    public async Task ObservationCasLoss_Should_ReturnDurableWinner()
    {
        var identity = SampleIdentity("attempt-1");

        // Branch 1: the race winner established a terminal receipt — the CAS loser
        // must replay it, never return a bare Conflict with no durable form.
        var (receiptWriter, receiptStore) = CreateWriterHarness();
        var winner = await receiptWriter.WriteTerminalAsync(
            identity, AgentToolPreDispatchReconciliationStatus.Conflict, null, default);
        var loser = new AgentToolPreDispatchResultWriter(receiptStore, TimeProvider.System, null);

        var replayed = await loser.WriteObservationAsync(
            identity, AgentToolPreDispatchReconciliationStatus.StillPending, null, default);

        replayed.Status.Should().Be(winner.Status);
        replayed.Receipt.Should().NotBeNull(
            "a CAS loser must replay the durable terminal receipt, never a bare Conflict.");
        replayed.Receipt!.IntegrityValue.Should().Be(winner.Receipt!.IntegrityValue);
        replayed.Observation.Should().BeNull();

        // Branch 2: no terminal receipt, but a concurrent writer advanced the
        // observation — the CAS loser must return the current durable observation.
        var (observationWriter, observationStore) = CreateWriterHarness();
        await observationWriter.WriteObservationAsync(
            identity, AgentToolPreDispatchReconciliationStatus.StillPending, "first", default);
        observationStore.FailNextObservationUpsert = true;

        var current = await observationWriter.WriteObservationAsync(
            identity, AgentToolPreDispatchReconciliationStatus.StillPending, "second", default);

        current.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.StillPending);
        current.Observation.Should().NotBeNull(
            "a CAS loser must return the current durable observation, never a bare Conflict.");
        current.Observation!.Revision.Should().BeGreaterThan(1);
        current.Receipt.Should().BeNull();
    }

    [Fact]
    public async Task ObservationCasLoss_WithoutDurableForm_Should_Throw()
    {
        var (resultWriter, store) = CreateWriterHarness();
        var identity = SampleIdentity("attempt-1");
        store.FailNextObservationUpsert = true;

        // No terminal receipt and no current observation can be read back — the
        // durable form is missing, so the writer must fail rather than fabricate
        // a protocol result with no durable backing.
        var act = async () => await resultWriter.WriteObservationAsync(
            identity, AgentToolPreDispatchReconciliationStatus.StillPending, null, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task TerminalInsertCasLossWithoutWinner_Should_NotReturnBareConflict()
    {
        var (resultWriter, store) = CreateWriterHarness();
        var identity = SampleIdentity("attempt-1");
        store.FailNextReceiptInsert = true;

        // Insert fails with no existing receipt — no durable winner to replay. The
        // writer must not fabricate a bare Conflict result without a durable form.
        var act = async () => await resultWriter.WriteTerminalAsync(
            identity, AgentToolPreDispatchReconciliationStatus.Conflict, null, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ObservationCasLoss_TerminalAppearsBetweenWinnerReads_Should_ReplayTerminal()
    {
        var (resultWriter, store) = CreateWriterHarness();
        var identity = SampleIdentity("attempt-1");

        // A concurrent reconciler already holds a StillPending observation.
        var existing = new AgentToolPreDispatchReconciliationObservation
        {
            Identity = identity,
            Status = AgentToolPreDispatchReconciliationStatus.StillPending,
            ReasonCode = "authority_unavailable",
            ObservedAt = DateTimeOffset.UtcNow,
            Revision = 1
        };
        (await store.TryUpsertObservationAsync(existing, 0)).Should().BeTrue();

        // The observation CAS loses to a concurrent revision bump, and a concurrent
        // reconciler commits a terminal receipt (removing the observation) between the
        // CAS-loser's first receipt read and its observation read — the TOCTOU window.
        store.FailNextObservationUpsert = true;
        store.ObservationReadsUntilTerminalAppears = 2;

        var result = await resultWriter.WriteObservationAsync(
            identity, AgentToolPreDispatchReconciliationStatus.StillPending, null, default);

        // The bounded convergence re-read must find the terminal winner and replay it
        // instead of misreporting a persistence inconsistency.
        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Conflict);
        result.Observation.Should().BeNull();
        result.Receipt.Should().NotBeNull();
        result.Receipt!.ReasonCode.Should().BeNull();
        result.Receipt!.IntegrityValue.Should().Be(store.InjectedTerminalReceipt!.IntegrityValue);
    }

    [Fact]
    public async Task Shared_NullReasonContractCases_Should_Pass_ThroughInMemoryDurableStore()
    {
        var identity1 = SampleIdentity("shared-null-reason-obs");
        var identity2 = SampleIdentity("shared-null-reason-receipt");
        var identity3 = SampleIdentity("shared-null-reason-restart");
        var driver = new InMemoryDurableContractDriver();

        await AgentToolPreDispatchReconciliationContractCases.NullReasonObservation_Should_RoundTripAsNull(
            driver, identity1, default);
        await AgentToolPreDispatchReconciliationContractCases.NullReasonTerminalReceipt_Should_RoundTripAsNull(
            driver, identity2, default);
        await AgentToolPreDispatchReconciliationContractCases.NullReasonTerminalReceipt_Should_ReplayAfterRestart(
            driver, identity3, default);
    }

    [Fact]
    public async Task BudgetFinalizeMismatch_Should_NotReturnBareTerminalResult()
    {
        var (executor, harness) = CreateExecutorHarness(AgentToolInvocationPreDispatchState.Accepted);
        // The budget authority finalizes to a different terminal state than the
        // requested Released — a deterministic conflict. The executor must classify
        // it as a terminal receipt, never a bare Conflict with no durable form.
        harness.BudgetGate.FinalizeResultState = AgentToolBudgetReservationState.Committed;

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Conflict);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.TerminalReceipt);
        result.ReasonCode.Should().BeNull();
        harness.Gate.CompleteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task BudgetFinalizeUnconfirmed_Should_NotReturnBareTerminalResult()
    {
        var (executor, harness) = CreateExecutorHarness(AgentToolInvocationPreDispatchState.Accepted);
        // Reservation still Reserved: the authority did not confirm the release.
        // This is retryable, so it must be an observation — still not a bare result.
        harness.BudgetGate.FinalizeResultState = AgentToolBudgetReservationState.Reserved;

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.StillPending);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.Observation);
        result.ReasonCode.Should().BeNull();
        harness.Gate.CompleteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task GateCompletionMismatch_Should_NotReturnBareTerminalResult()
    {
        var (executor, harness) = CreateExecutorHarness(AgentToolInvocationPreDispatchState.Accepted);
        // The Gate does not reach the requested terminal state. The executor must
        // classify the actual outcome into a durable form instead of returning a
        // bare conflict.
        harness.Gate.CompleteResultState = AgentToolInvocationPreDispatchState.ReconciliationPending;

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.StillPending);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.Observation);
        result.ReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task GateCompletionMismatch_DispatchStarted_Should_BePostDispatchUnknownReceipt()
    {
        var (executor, harness) = CreateExecutorHarness(AgentToolInvocationPreDispatchState.Accepted);
        harness.Gate.CompleteResultState = AgentToolInvocationPreDispatchState.DispatchStarted;

        var result = await executor.ExecuteAsync(
            SampleIdentity("attempt-1"),
            ReleasedDecision(),
            Snapshot(harness.Gate.CurrentState, AgentToolBudgetReadStatus.Reserved, AgentToolGovernancePreDispatchReadStatus.Accepted),
            context: null);

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown);
        result.Persistence.Should().Be(AgentToolPreDispatchSettlementPersistence.TerminalReceipt);
        result.ReasonCode.Should().Be("dispatch_started");
    }

    [Fact]
    public async Task CheckpointValidationConflict_Should_PersistTerminalReceipt()
    {
        var (reconciler, harness) = CreateReconcilerHarness(
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Accepted);
        // The checkpoint does not belong to this attempt identity → validation conflict.
        harness.Auditor.CheckpointOverride = StubCheckpoint with
        {
            Context = StubContext with { AttemptId = "other-attempt" }
        };

        var result = await reconciler.ReconcileAsync(SampleIdentity("attempt-1"));

        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Conflict);
        result.Receipt.Should().NotBeNull(
            "a checkpoint-validation conflict is a terminal disposition and must be persisted as an immutable receipt.");
        result.Receipt!.ReasonCode.Should().BeNull();
        result.Observation.Should().BeNull();

        var persisted = await harness.Store.ReadReceiptAsync(SampleIdentity("attempt-1"));
        persisted.Should().NotBeNull("the terminal receipt must be durable, not just returned.");
        persisted!.ReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task RepeatedValidationConflict_Should_ReplaySameReceipt()
    {
        var (reconciler, harness) = CreateReconcilerHarness(
            AgentToolInvocationPreDispatchState.Accepted,
            AgentToolBudgetReadStatus.Reserved,
            AgentToolGovernancePreDispatchReadStatus.Accepted);
        harness.Auditor.CheckpointOverride = StubCheckpoint with
        {
            Context = StubContext with { AttemptId = "other-attempt" }
        };
        var identity = SampleIdentity("attempt-1");

        var first = await reconciler.ReconcileAsync(identity);
        var second = await reconciler.ReconcileAsync(identity);

        first.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Conflict);
        second.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Conflict);
        second.Receipt.Should().NotBeNull();
        second.Receipt!.IntegrityValue.Should().Be(first.Receipt!.IntegrityValue);
        second.Receipt.ReasonCode.Should().BeNull();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────

    private static AgentToolPreDispatchIdentity SampleIdentity(string attemptId)
        => new(SampleKey(), attemptId);

    private static AgentToolLogicalInvocationKey SampleKey()
        => new("tenant", "user", "agent", "execution", "invocation");

    private static (AgentToolPreDispatchResultWriter writer, InMemoryContractStore store) CreateWriterHarness()
    {
        var store = new InMemoryContractStore();
        var writer = new AgentToolPreDispatchResultWriter(store, TimeProvider.System, null);
        return (writer, store);
    }

    private static (AgentToolPreDispatchSettlementExecutor executor, SettlementContractHarness harness) CreateExecutorHarness(
        AgentToolInvocationPreDispatchState gateState)
    {
        var harness = new SettlementContractHarness(gateState);
        var executor = new AgentToolPreDispatchSettlementExecutor(
            harness.Gate,
            harness.BudgetGate,
            harness.Auditor);
        return (executor, harness);
    }

    private static (DefaultAgentToolPreDispatchReconciler reconciler, ReconcilerContractHarness harness) CreateReconcilerHarness(
        AgentToolInvocationPreDispatchState gateState,
        AgentToolBudgetReadStatus budgetStatus,
        AgentToolGovernancePreDispatchReadStatus checkpointStatus)
    {
        var harness = new ReconcilerContractHarness(gateState, budgetStatus, checkpointStatus);
        var reconciler = new DefaultAgentToolPreDispatchReconciler(
            harness.Gate,
            harness.BudgetGate,
            harness.Auditor,
            harness.Store,
            TimeProvider.System,
            null);
        return (reconciler, harness);
    }

    private static AgentToolPreDispatchRecoveryDecision ReleasedDecision()
        => new()
        {
            Disposition = AgentToolPreDispatchReconciliationStatus.Released,
            GateAction = AgentToolPreDispatchGateAction.ClaimAndRelease,
            BudgetAction = AgentToolPreDispatchBudgetAction.FinalizeReleased,
            GovernanceAction = AgentToolPreDispatchGovernanceAction.FinalizeReleasedNoDispatch,
            ReasonCode = "released_no_dispatch"
        };

    private static AgentToolPreDispatchAuthoritySnapshot Snapshot(
        AgentToolInvocationPreDispatchState gateState,
        AgentToolBudgetReadStatus budgetStatus,
        AgentToolGovernancePreDispatchReadStatus checkpointStatus)
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

    private sealed class InMemoryContractStore : IAgentToolPreDispatchReconciliationStore
    {
        private readonly Dictionary<string, AgentToolPreDispatchReconciliationObservation> _observations = new();
        private readonly Dictionary<string, AgentToolPreDispatchReconciliationReceipt> _receipts = new();
        private readonly object _lock = new();

        // CAS-failure injection: when set, the next observation upsert / receipt insert
        // fails as if a concurrent reconciler won the race (or the store rejected it).
        public bool FailNextObservationUpsert { get; set; }
        public bool FailNextReceiptInsert { get; set; }

        // TOCTOU injection: when > 0, each observation read counts down, and on the read
        // where it reaches 0 a concurrent reconciler appears to commit a terminal receipt
        // (and remove the observation) — exactly between the CAS-loser's winner reads.
        public int ObservationReadsUntilTerminalAppears { get; set; }
        public AgentToolPreDispatchReconciliationReceipt? InjectedTerminalReceipt { get; private set; }

        public ValueTask<AgentToolPreDispatchReconciliationObservation?> ReadObservationAsync(
            AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (ObservationReadsUntilTerminalAppears > 0)
                {
                    ObservationReadsUntilTerminalAppears--;
                    if (ObservationReadsUntilTerminalAppears == 0)
                    {
                        InjectedTerminalReceipt = new AgentToolPreDispatchReconciliationReceipt
                        {
                            Identity = identity,
                            Status = AgentToolPreDispatchReconciliationStatus.Conflict,
                            ReasonCode = null,
                            TerminalAt = DateTimeOffset.UtcNow,
                            IntegrityValue = "integrity-terminal-appears-between-reads"
                        };
                        _receipts[identity.AttemptId] = InjectedTerminalReceipt;
                        _observations.Remove(identity.AttemptId);
                    }
                }

                _observations.TryGetValue(identity.AttemptId, out var obs);
                return ValueTask.FromResult(obs);
            }
        }

        public ValueTask<bool> TryUpsertObservationAsync(
            AgentToolPreDispatchReconciliationObservation observation, long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_receipts.ContainsKey(observation.Identity.AttemptId))
                    return ValueTask.FromResult(false);

                if (FailNextObservationUpsert)
                {
                    FailNextObservationUpsert = false;
                    // Simulate a concurrent writer that advanced the revision before this CAS.
                    if (_observations.TryGetValue(observation.Identity.AttemptId, out var newer))
                        _observations[observation.Identity.AttemptId] = newer with { Revision = newer.Revision + 1 };
                    return ValueTask.FromResult(false);
                }

                if (_observations.TryGetValue(observation.Identity.AttemptId, out var existing))
                {
                    if (existing.Revision != expectedRevision)
                        return ValueTask.FromResult(false);
                }
                _observations[observation.Identity.AttemptId] = observation;
                return ValueTask.FromResult(true);
            }
        }

        public ValueTask<AgentToolPreDispatchReconciliationReceipt?> ReadReceiptAsync(
            AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _receipts.TryGetValue(identity.AttemptId, out var receipt);
                return ValueTask.FromResult(receipt);
            }
        }

        public ValueTask<bool> TryInsertReceiptAsync(
            AgentToolPreDispatchReconciliationReceipt receipt, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_receipts.ContainsKey(receipt.Identity.AttemptId))
                {
                    _observations.Remove(receipt.Identity.AttemptId);
                    return ValueTask.FromResult(false);
                }

                if (FailNextReceiptInsert)
                {
                    FailNextReceiptInsert = false;
                    // Simulate a store that rejected the insert with no existing receipt and
                    // no other durable form — nothing was durably established.
                    return ValueTask.FromResult(false);
                }

                _receipts[receipt.Identity.AttemptId] = receipt;
                _observations.Remove(receipt.Identity.AttemptId);
                return ValueTask.FromResult(true);
            }
        }
    }

    /// <summary>
    /// In-memory durable driver used to run the shared persistence contract cases
    /// locally without PostgreSQL. The store object is the durable database, so a
    /// "restart" is a no-op; the contract cases never touch the auditor/gates.
    /// </summary>
    private sealed class InMemoryDurableContractDriver : IDurableAgentToolPreDispatchContractDriver
    {
        public InMemoryContractStore Store { get; } = new();

        IAgentToolGovernanceAuditor IAgentToolPreDispatchContractDriver.Auditor => null!;
        IAgentToolBudgetGate IAgentToolPreDispatchContractDriver.BudgetGate => null!;
        IAgentToolInvocationGate IAgentToolPreDispatchContractDriver.InvocationGate => null!;
        IAgentToolPreDispatchReconciliationStore IDurableAgentToolPreDispatchContractDriver.ReconciliationStore => Store;

        ValueTask IDurableAgentToolPreDispatchContractDriver.RestartProviderAsync(
            CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class SettlementContractHarness
    {
        public ContractStubGate Gate { get; }
        public ContractStubBudgetGate BudgetGate { get; }
        public ContractStubAuditor Auditor { get; }

        public SettlementContractHarness(AgentToolInvocationPreDispatchState gateState)
        {
            Gate = new ContractStubGate(gateState);
            BudgetGate = new ContractStubBudgetGate();
            Auditor = new ContractStubAuditor();
        }
    }

    private sealed class ContractStubGate : IAgentToolInvocationGate
    {
        public AgentToolInvocationPreDispatchState CurrentState { get; private set; }
        public AgentToolInvocationPreDispatchState? CompleteResultState { get; set; }
        public int CompleteCallCount { get; private set; }

        public ContractStubGate(AgentToolInvocationPreDispatchState state) => CurrentState = state;

        public ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateAsync(
            AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = CurrentState,
                Intent = StubIntent,
                BoundReservationId = CurrentState is AgentToolInvocationPreDispatchState.Ready
                    or AgentToolInvocationPreDispatchState.Accepted
                    ? "stub-reservation"
                    : null,
                AcceptedReceipt = CurrentState == AgentToolInvocationPreDispatchState.Accepted
                    ? StubReceipt
                    : null,
                Revision = 1,
                ReasonCode = "stub"
            });
        }

        public ValueTask<AgentToolPreDispatchReconciliationClaimResult> TryBeginPreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaimRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new AgentToolPreDispatchReconciliationClaimResult
            {
                Status = AgentToolPreDispatchReconciliationClaimStatus.Claimed,
                Claim = new AgentToolPreDispatchReconciliationClaim
                {
                    Identity = request.Identity,
                    Revision = 2,
                    ClaimToken = "rc-stub-1",
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
            if (CompleteResultState.HasValue)
            {
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = CompleteResultState.Value,
                    ReasonCode = "completion_mismatch"
                });
            }

            CurrentState = kind == AgentToolPreDispatchReconciliationCompletionKind.Abandoned
                ? AgentToolInvocationPreDispatchState.Abandoned
                : AgentToolInvocationPreDispatchState.Released;
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = CurrentState,
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

    private sealed class ContractStubBudgetGate : IAgentToolBudgetGate
    {
        public AgentToolBudgetReservationState FinalizeResultState { get; set; } = AgentToolBudgetReservationState.Released;

        public ValueTask<AgentToolBudgetReservation> FinalizeAsync(
            AgentToolBudgetFinalizeRequest request, CancellationToken cancellationToken = default)
        {
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

    private sealed class ContractStubAuditor : IAgentToolGovernanceAuditor
    {
        public AgentToolGovernancePreDispatchRecord? CheckpointOverride { get; set; }
        public AgentToolGovernancePreDispatchReadStatus CheckpointStatus { get; set; } = AgentToolGovernancePreDispatchReadStatus.Accepted;

        public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(AgentToolGovernancePreDispatchRecord record, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new AgentToolGovernancePreDispatchReadResult
            {
                Status = CheckpointStatus,
                Receipt = CheckpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted ? StubReceipt : null,
                Checkpoint = CheckpointStatus == AgentToolGovernancePreDispatchReadStatus.Accepted
                    ? CheckpointOverride ?? StubCheckpoint
                    : null
            });
        }
        public ValueTask RecordDecisionAsync(AgentToolGovernanceDecisionRecord decision, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(AgentToolGovernanceFinalizationRecord record, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentToolGovernanceFinalizationResult
            {
                Status = AgentToolGovernanceFinalizationStatus.Finalized,
                Record = record
            });
        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(string auditId, string? tenantId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class ReconcilerContractHarness
    {
        public ContractStubGate Gate { get; }
        public ContractReconcilerBudgetGate BudgetGate { get; }
        public ContractStubAuditor Auditor { get; }
        public InMemoryContractStore Store { get; }

        public ReconcilerContractHarness(
            AgentToolInvocationPreDispatchState gateState,
            AgentToolBudgetReadStatus budgetStatus,
            AgentToolGovernancePreDispatchReadStatus checkpointStatus)
        {
            Gate = new ContractStubGate(gateState);
            BudgetGate = new ContractReconcilerBudgetGate(budgetStatus);
            Auditor = new ContractStubAuditor { CheckpointStatus = checkpointStatus };
            Store = new InMemoryContractStore();
        }
    }

    private sealed class ContractReconcilerBudgetGate : IAgentToolBudgetGate
    {
        private readonly AgentToolBudgetReadStatus _status;

        public ContractReconcilerBudgetGate(AgentToolBudgetReadStatus status) => _status = status;

        public ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            var state = _status switch
            {
                AgentToolBudgetReadStatus.Reserved => AgentToolBudgetReservationState.Reserved,
                AgentToolBudgetReadStatus.Released => AgentToolBudgetReservationState.Released,
                AgentToolBudgetReadStatus.Committed => AgentToolBudgetReservationState.Committed,
                _ => (AgentToolBudgetReservationState?)null
            };
            return ValueTask.FromResult(new AgentToolBudgetReservationReadResult
            {
                Status = _status,
                Reservation = state.HasValue ? StubReservation(state.Value) : null
            });
        }

        public ValueTask<AgentToolBudgetReserveResult> ReserveAsync(AgentToolBudgetReserveRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolBudgetReservation> FinalizeAsync(AgentToolBudgetFinalizeRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
