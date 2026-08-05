using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Invocation;

/// <summary>
/// Slice 3 Red tests: Pre-dispatch state machine transitions and recovery
/// (F07–F13, F25, F26, F29, H09, H10, B06, B07, B13–B15, B17, C01, C02, C11).
/// </summary>
public sealed class AgentToolPreDispatchStateMachineTests
{
    [Fact]
    public async Task F07_PreparePreDispatchIntent_TransitionsUnknownToPending()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));

        var result = await gate.PreparePreDispatchIntentAsync(
            acquired.Lease!,
            new AgentToolInvocationPreparePreDispatchIntentRequest
            {
                Intent = SampleIntent(acquired.Lease!, "fp-a")
            });

        result.State.Should().Be(AgentToolInvocationPreDispatchState.Pending);
        result.Intent.Should().NotBeNull();
    }

    [Fact]
    public async Task F08_BindReservation_TransitionsPendingToReady()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));
        await gate.PreparePreDispatchIntentAsync(acquired.Lease!,
            new AgentToolInvocationPreparePreDispatchIntentRequest
            {
                Intent = SampleIntent(acquired.Lease!, "fp-a")
            });

        var result = await gate.BindPreDispatchReservationAsync(
            acquired.Lease!,
            new AgentToolInvocationBindReservationRequest
            {
                ReservationId = "res-1",
                Reservation = SampleReservation(acquired.Lease!, "fp-a")
            });

        result.State.Should().Be(AgentToolInvocationPreDispatchState.Ready);
        result.BoundReservationId.Should().Be("res-1");
    }

    [Fact]
    public async Task F09_BindAccepted_TransitionsReadyToAccepted()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));
        await PreparePendingAndReservationAsync(gate, acquired.Lease!, "fp-a", "res-1");

        var result = await gate.BindAcceptedPreDispatchAsync(
            acquired.Lease!,
            new AgentToolInvocationBindPreDispatchRequest
            {
                Receipt = TestReceipt(acquired.Lease!)
            });

        result.State.Should().Be(AgentToolInvocationPreDispatchState.Accepted);
        result.AcceptedReceipt.Should().NotBeNull();
    }

    [Fact]
    public async Task F10_BindReservation_RejectsWhenNotPending()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));

        var result = await gate.BindPreDispatchReservationAsync(
            acquired.Lease!,
            new AgentToolInvocationBindReservationRequest
            {
                ReservationId = "res-1",
                Reservation = SampleReservation(acquired.Lease!, "fp-a")
            });

        result.State.Should().Be(AgentToolInvocationPreDispatchState.Unknown);
        result.ReasonCode.Should().Be("pre_dispatch_not_pending");
    }

    [Fact]
    public async Task F11_BindAccepted_RejectsWhenNotReady()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));
        await gate.PreparePreDispatchIntentAsync(acquired.Lease!,
            new AgentToolInvocationPreparePreDispatchIntentRequest
            {
                Intent = SampleIntent(acquired.Lease!, "fp-a")
            });

        var result = await gate.BindAcceptedPreDispatchAsync(
            acquired.Lease!,
            new AgentToolInvocationBindPreDispatchRequest
            {
                Receipt = TestReceipt(acquired.Lease!)
            });

        result.State.Should().Be(AgentToolInvocationPreDispatchState.Pending);
        result.ReasonCode.Should().Be("pre_dispatch_not_ready");
    }

    [Fact]
    public async Task F12_DispatchStarted_RequiresAcceptedState()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));
        await PrepareThroughReadyAsync(gate, acquired.Lease!, "fp-a", "res-1");

        var started = await gate.TryMarkDispatchStartedAsync(
            acquired.Lease!, TestReceipt(acquired.Lease!), "res-1");

        started.Should().BeFalse();
    }

    [Fact]
    public async Task F13_DispatchStarted_RequiresMatchingReceipt()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));
        await PrepareFullPreDispatchAsync(gate, acquired.Lease!, "fp-a", "res-1");

        var wrongReceipt = TestReceipt(acquired.Lease!) with { AuditId = "wrong-audit" };

        var started = await gate.TryMarkDispatchStartedAsync(
            acquired.Lease!, wrongReceipt, "res-1");

        started.Should().BeFalse();
    }

    [Fact]
    public async Task F25_PreparePreDispatchIntent_IsIdempotentForSameLease()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));

        var first = await gate.PreparePreDispatchIntentAsync(
            acquired.Lease!,
            new AgentToolInvocationPreparePreDispatchIntentRequest
            {
                Intent = SampleIntent(acquired.Lease!, "fp-a")
            });

        var second = await gate.PreparePreDispatchIntentAsync(
            acquired.Lease!,
            new AgentToolInvocationPreparePreDispatchIntentRequest
            {
                Intent = SampleIntent(acquired.Lease!, "fp-a")
            });

        first.State.Should().Be(AgentToolInvocationPreDispatchState.Pending);
        second.State.Should().Be(AgentToolInvocationPreDispatchState.Pending);
    }

    [Fact]
    public async Task PreparePreDispatchIntent_ChangedRetry_Should_Conflict_WithoutReplacingFrozenIntent()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));
        var identity = SampleIdentity(acquired.Lease!.AttemptId);
        var original = SampleIntent(acquired.Lease, "fp-a");

        await gate.PreparePreDispatchIntentAsync(
            acquired.Lease,
            new AgentToolInvocationPreparePreDispatchIntentRequest { Intent = original });

        var changed = original with
        {
            Approval = original.Approval with { ReasonCode = "changed" }
        };
        var conflict = await gate.PreparePreDispatchIntentAsync(
            acquired.Lease,
            new AgentToolInvocationPreparePreDispatchIntentRequest { Intent = changed });

        conflict.State.Should().Be(AgentToolInvocationPreDispatchState.Unknown);
        var recovered = await gate.GetPreDispatchStateAsync(identity);
        AgentToolGovernancePreDispatchComparer.Equivalent(recovered.Intent!, original)
            .Should().BeTrue();
    }

    [Fact]
    public async Task BindReservation_ChangedRetry_Should_Conflict_WithoutReplacingBinding()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));
        await gate.PreparePreDispatchIntentAsync(
            acquired.Lease!,
            new AgentToolInvocationPreparePreDispatchIntentRequest
            {
                Intent = SampleIntent(acquired.Lease!, "fp-a")
            });

        var original = SampleReservation(acquired.Lease!, "fp-a");
        await gate.BindPreDispatchReservationAsync(
            acquired.Lease!,
            new AgentToolInvocationBindReservationRequest
            {
                ReservationId = original.ReservationId,
                Reservation = original
            });

        var conflict = await gate.BindPreDispatchReservationAsync(
            acquired.Lease!,
            new AgentToolInvocationBindReservationRequest
            {
                ReservationId = original.ReservationId,
                Reservation = original with { CostUnits = original.CostUnits + 1 }
            });

        conflict.State.Should().Be(AgentToolInvocationPreDispatchState.Unknown);
        var retry = await gate.BindPreDispatchReservationAsync(
            acquired.Lease!,
            new AgentToolInvocationBindReservationRequest
            {
                ReservationId = original.ReservationId,
                Reservation = original
            });
        retry.State.Should().Be(AgentToolInvocationPreDispatchState.Ready);
    }

    [Fact]
    public async Task BindAccepted_ChangedRetry_Should_Conflict_WithoutReplacingReceipt()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));
        await PrepareThroughReadyAsync(gate, acquired.Lease!, "fp-a", "res-1");
        var original = TestReceipt(acquired.Lease!);

        await gate.BindAcceptedPreDispatchAsync(
            acquired.Lease!,
            new AgentToolInvocationBindPreDispatchRequest { Receipt = original });

        var conflict = await gate.BindAcceptedPreDispatchAsync(
            acquired.Lease!,
            new AgentToolInvocationBindPreDispatchRequest
            {
                Receipt = original with { AuditId = "changed-audit" }
            });

        conflict.State.Should().Be(AgentToolInvocationPreDispatchState.Unknown);
        var recovered = await gate.GetPreDispatchStateAsync(
            SampleIdentity(acquired.Lease!.AttemptId));
        AgentToolGovernancePreDispatchComparer.Equivalent(
                recovered.AcceptedReceipt!,
                original)
            .Should().BeTrue();
    }

    [Fact]
    public async Task F26_GetPreDispatchState_ReturnsCurrentStateAfterRecovery()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));
        await PrepareFullPreDispatchAsync(gate, acquired.Lease!, "fp-a", "res-1");

        var state = await gate.GetPreDispatchStateAsync(SampleIdentity(acquired.Lease!.AttemptId));

        state.State.Should().Be(AgentToolInvocationPreDispatchState.Accepted);
        state.BoundReservationId.Should().Be("res-1");
        state.AcceptedReceipt.Should().NotBeNull();
    }

    [Fact]
    public async Task Known_BudgetDenial_Should_Abandon_Attempt_WithStableReceipt()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));
        await gate.PreparePreDispatchIntentAsync(acquired.Lease!, new AgentToolInvocationPreparePreDispatchIntentRequest
        {
            Intent = SampleIntent(acquired.Lease!, "fp-a")
        });

        var result = await gate.PublishBudgetDenialAsync(
            acquired.Lease!,
            new AgentToolInvocationPublishDenialRequest
            {
                Outcome = new AgentToolInvocationOutcome
                {
                    Kind = AgentToolInvocationOutcomeKind.GovernanceDenied,
                    Code = "budget_denied",
                    Message = "Budget exceeded"
                },
                ReasonCode = "budget_denied"
            });

        result.State.Should().Be(AgentToolInvocationPreDispatchState.Abandoned);
        result.AbandonedReceipt.Should().NotBeNull();
        result.AbandonedReceipt!.ReasonCode.Should().Be("budget_denied");
    }

    [Fact]
    public async Task H09_DispatchCAS_RequiresAcceptedReceipt()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var acquired = await gate.AcquireAsync(Request("fp-a"));

        var started = await gate.TryMarkDispatchStartedAsync(
            acquired.Lease!, TestReceipt(acquired.Lease!), "res-1");

        started.Should().BeFalse();
    }

    [Fact]
    public async Task H10_LeaseExpiry_DuringPreDispatch_FailsDispatch()
    {
        var time = new ManualTimeProvider();
        var gate = new DevelopmentInMemoryAgentToolInvocationGate(time, TimeSpan.FromSeconds(10));
        var acquired = await gate.AcquireAsync(Request("fp-a"));
        await PrepareFullPreDispatchAsync(gate, acquired.Lease!, "fp-a", "res-1");

        time.Advance(TimeSpan.FromSeconds(11));

        var started = await gate.TryMarkDispatchStartedAsync(
            acquired.Lease!, TestReceipt(acquired.Lease!), "res-1");

        started.Should().BeFalse();
    }

    [Fact]
    public async Task Pending_Checkpoint_Should_Block_LeaseExpiryReplacement()
    {
        var time = new ManualTimeProvider();
        var gate = new DevelopmentInMemoryAgentToolInvocationGate(time, TimeSpan.FromSeconds(10));
        var first = await gate.AcquireAsync(Request("fp-a"));
        await gate.PreparePreDispatchIntentAsync(first.Lease!, new AgentToolInvocationPreparePreDispatchIntentRequest
        {
            Intent = SampleIntent(first.Lease!, "fp-a")
        });

        time.Advance(TimeSpan.FromSeconds(11));
        var retry = await gate.AcquireAsync(Request("fp-a"));

        retry.Status.Should().Be(AgentToolInvocationAcquireStatus.Indeterminate);
        retry.Lease.Should().BeNull();
        (await gate.GetPreDispatchStateAsync(SampleIdentity(first.Lease!.AttemptId)))
            .State.Should().Be(AgentToolInvocationPreDispatchState.Pending);
    }

    [Fact]
    public async Task Ready_Checkpoint_Should_Block_LeaseExpiryReplacement()
    {
        var time = new ManualTimeProvider();
        var gate = new DevelopmentInMemoryAgentToolInvocationGate(time, TimeSpan.FromSeconds(10));
        var first = await gate.AcquireAsync(Request("fp-a"));
        await PrepareThroughReadyAsync(gate, first.Lease!, "fp-a", "res-1");

        time.Advance(TimeSpan.FromSeconds(11));
        var retry = await gate.AcquireAsync(Request("fp-a"));

        retry.Status.Should().Be(AgentToolInvocationAcquireStatus.Indeterminate);
        retry.Lease.Should().BeNull();
        (await gate.GetPreDispatchStateAsync(SampleIdentity(first.Lease!.AttemptId)))
            .State.Should().Be(AgentToolInvocationPreDispatchState.Ready);
    }

    [Fact]
    public async Task Accepted_Checkpoint_Should_Block_LeaseExpiryReplacement()
    {
        var time = new ManualTimeProvider();
        var gate = new DevelopmentInMemoryAgentToolInvocationGate(time, TimeSpan.FromSeconds(10));
        var first = await gate.AcquireAsync(Request("fp-a"));
        await PrepareFullPreDispatchAsync(gate, first.Lease!, "fp-a", "res-1");

        time.Advance(TimeSpan.FromSeconds(11));
        var retry = await gate.AcquireAsync(Request("fp-a"));

        retry.Status.Should().Be(AgentToolInvocationAcquireStatus.Indeterminate);
        retry.Lease.Should().BeNull();
        (await gate.GetPreDispatchStateAsync(SampleIdentity(first.Lease!.AttemptId)))
            .State.Should().Be(AgentToolInvocationPreDispatchState.Accepted);
    }

    [Fact]
    public async Task Acquire_AfterBudgetDenial_Should_Create_NewAttempt()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var first = await gate.AcquireAsync(Request("fp-a"));
        await gate.PreparePreDispatchIntentAsync(first.Lease!, new AgentToolInvocationPreparePreDispatchIntentRequest
        {
            Intent = SampleIntent(first.Lease!, "fp-a")
        });
        var denial = new AgentToolInvocationPublishDenialRequest
        {
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.GovernanceDenied,
                Code = "budget_denied",
                Message = "budget_denied"
            },
            ReasonCode = "budget_denied"
        };
        var abandoned = await gate.PublishBudgetDenialAsync(first.Lease!, denial);

        var second = await gate.AcquireAsync(Request("fp-a"));

        second.Status.Should().Be(AgentToolInvocationAcquireStatus.Acquired);
        second.Lease!.AttemptId.Should().NotBe(first.Lease!.AttemptId);
        var oldAttempt = await gate.GetPreDispatchStateAsync(SampleIdentity(first.Lease.AttemptId));
        oldAttempt.State.Should().Be(AgentToolInvocationPreDispatchState.Abandoned);
        oldAttempt.AbandonedReceipt.Should().BeEquivalentTo(abandoned.AbandonedReceipt);
        (await gate.GetPreDispatchStateAsync(SampleIdentity(second.Lease.AttemptId)))
            .State.Should().Be(AgentToolInvocationPreDispatchState.Unknown);
    }

    [Fact]
    public async Task B06_BudgetReserve_IsAttemptIdempotent()
    {
        var gate = new DevelopmentInMemoryAgentToolBudgetGate();
        var context = SampleBudgetContext("attempt-1");

        var first = await gate.ReserveAsync(new AgentToolBudgetReserveRequest { Context = context });
        var second = await gate.ReserveAsync(new AgentToolBudgetReserveRequest { Context = context });

        first.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);
        second.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);
        second.Reservation!.ReservationId.Should().Be(first.Reservation!.ReservationId);
    }

    [Fact]
    public async Task B07_BudgetReserve_DifferentAttempt_GetsDifferentReservation()
    {
        var gate = new DevelopmentInMemoryAgentToolBudgetGate();

        var first = await gate.ReserveAsync(new AgentToolBudgetReserveRequest { Context = SampleBudgetContext("attempt-1") });
        var second = await gate.ReserveAsync(new AgentToolBudgetReserveRequest { Context = SampleBudgetContext("attempt-2") });

        first.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);
        second.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);
        second.Reservation!.ReservationId.Should().NotBe(first.Reservation!.ReservationId);
    }

    [Fact]
    public async Task B13_BudgetGetReservationState_ReturnsReservedState()
    {
        var gate = new DevelopmentInMemoryAgentToolBudgetGate();
        var identity = SampleIdentity("attempt-1");
        await gate.ReserveAsync(new AgentToolBudgetReserveRequest { Context = SampleBudgetContext("attempt-1") });

        var state = await gate.GetReservationStateAsync(identity);

        state.Status.Should().Be(AgentToolBudgetReadStatus.Reserved);
        state.Reservation.Should().NotBeNull();
    }

    [Fact]
    public async Task B14_BudgetGetReservationState_ReturnsMissingForUnknownAttempt()
    {
        var gate = new DevelopmentInMemoryAgentToolBudgetGate();
        var identity = SampleIdentity("unknown-attempt");

        var state = await gate.GetReservationStateAsync(identity);

        state.Status.Should().Be(AgentToolBudgetReadStatus.Missing);
        state.Reservation.Should().BeNull();
    }

    [Fact]
    public async Task B15_BudgetReserve_DoesNotDoubleCountForSameAttempt()
    {
        var gate = new DevelopmentInMemoryAgentToolBudgetGate();
        var identity = SampleIdentity("attempt-1");
        var context = SampleBudgetContext("attempt-1");

        await gate.ReserveAsync(new AgentToolBudgetReserveRequest { Context = context });
        await gate.ReserveAsync(new AgentToolBudgetReserveRequest { Context = context });

        var state = await gate.GetReservationStateAsync(identity);
        state.Status.Should().Be(AgentToolBudgetReadStatus.Reserved);
    }

    [Fact]
    public async Task B17_BudgetReserve_RejectsConflictingRequirementForSameAttempt()
    {
        var gate = new DevelopmentInMemoryAgentToolBudgetGate();
        var firstContext = SampleBudgetContext("attempt-1");
        var conflictingContext = firstContext with
        {
            Governance = firstContext.Governance with { Budget = firstContext.Governance.Budget with { CostUnits = 99 } }
        };

        var first = await gate.ReserveAsync(new AgentToolBudgetReserveRequest { Context = firstContext });
        var second = await gate.ReserveAsync(new AgentToolBudgetReserveRequest { Context = conflictingContext });

        first.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);
        second.Status.Should().Be(AgentToolBudgetReserveStatus.Denied);
    }

    [Fact]
    public void C01_Reconciler_Contract_Exists()
    {
        typeof(IAgentToolPreDispatchReconciler).Should().NotBeNull();
        typeof(IAgentToolPreDispatchReconciler).GetMethod("ReconcileAsync").Should().NotBeNull();
    }

    [Fact]
    public void C02_ReconciliationStore_Contract_Exists()
    {
        typeof(IAgentToolPreDispatchReconciliationStore).Should().NotBeNull();
        typeof(IAgentToolPreDispatchReconciliationStore).GetMethod("TryUpsertObservationAsync").Should().NotBeNull();
        typeof(IAgentToolPreDispatchReconciliationStore).GetMethod("TryInsertReceiptAsync").Should().NotBeNull();
    }

    [Fact]
    public void C11_ReconciliationResult_HasCorrectStatusValues()
    {
        Enum.GetNames<AgentToolPreDispatchReconciliationStatus>()
            .Should().Contain(new[]
            {
                "Unknown",
                "Released",
                "StillPending",
                "Conflict",
                "Missing"
            });
    }

    // --- Helpers ---

    private static AgentToolInvocationAcquireRequest Request(string fingerprint)
        => new(
            new AgentToolLogicalInvocationKey("tenant", "user", "agent", "execution", "invocation"),
            fingerprint);

    private static AgentToolPreDispatchIdentity SampleIdentity(string attemptId)
        => new(
            new AgentToolLogicalInvocationKey("tenant", "user", "agent", "execution", "invocation"),
            attemptId);

    private static AgentToolInvocationPreDispatchIntentSnapshot SampleIntent(
        AgentToolInvocationLease lease, string fingerprint)
        => new()
        {
            FrozenLease = lease,
            InvocationFingerprint = fingerprint,
            Context = new AgentToolGovernanceAuditContext
            {
                LogicalInvocationKey = SampleIdentity(lease.AttemptId).LogicalInvocationKey,
                AttemptId = lease.AttemptId,
                InvocationFingerprint = fingerprint,
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
                    SampleBudgetRequirement(),
                    AgentToolAuditMode.Required)
            },
            Approval = new AgentToolApprovalResult
            {
                Decision = AgentToolApprovalDecision.NotRequired,
                ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable
            }
        };

    private static AgentToolBudgetReservation SampleReservation(
        AgentToolInvocationLease lease, string fingerprint)
        => new()
        {
            ReservationId = "res-1",
            AttemptId = lease.AttemptId,
            InvocationFingerprint = fingerprint,
            Category = "default",
            CostUnits = 1,
            MaxCallsPerExecution = 10,
            State = AgentToolBudgetReservationState.Reserved
        };

    private static AgentToolGovernancePreDispatchReceipt TestReceipt(AgentToolInvocationLease lease)
        => new()
        {
            Identity = SampleIdentity(lease.AttemptId),
            AuditId = "audit-test",
            AcceptedAt = DateTimeOffset.UtcNow
        };

    private static AgentToolBudgetRequirement SampleBudgetRequirement()
        => new()
        {
            Category = "default",
            CostUnits = 1,
            MaxCallsPerExecution = 10
        };

    private static AgentToolGovernanceContext SampleBudgetContext(string attemptId)
        => new()
        {
            LogicalInvocationKey = new AgentToolLogicalInvocationKey("tenant", "user", "agent", "execution", "invocation"),
            AttemptId = attemptId,
            InvocationFingerprint = "fp-budget",
            ArgumentsHash = "args-hash",
            ExecutionContext = new AgentExecutionContext
            {
                ExecutionId = "execution",
                InvocationId = "invocation",
                AgentId = "agent",
                AgentRoles = new HashSet<string> { "role-1" },
                CallOrigin = AgentToolCallOrigin.ExplicitRequest
            },
            ToolContract = new AgentToolContractIdentity("tool", 1, "hash"),
            CapabilityContract = new AgentToolContractIdentity("cap", 1, "hash"),
            Governance = new AgentToolEffectiveGovernance(
                AgentToolSelectionPolicy.ExplicitOnly,
                AgentToolSideEffectKind.ReadOnly,
                CapabilityRiskLevel.Low,
                AgentToolApprovalMode.None,
                SampleBudgetRequirement(),
                AgentToolAuditMode.Required)
        };

    private static async Task PrepareFullPreDispatchAsync(
        DevelopmentInMemoryAgentToolInvocationGate gate,
        AgentToolInvocationLease lease,
        string fingerprint,
        string reservationId)
    {
        await PreparePendingAndReservationAsync(gate, lease, fingerprint, reservationId);
        await gate.BindAcceptedPreDispatchAsync(lease, new AgentToolInvocationBindPreDispatchRequest
        {
            Receipt = TestReceipt(lease)
        });
    }

    private static async Task PreparePendingAndReservationAsync(
        DevelopmentInMemoryAgentToolInvocationGate gate,
        AgentToolInvocationLease lease,
        string fingerprint,
        string reservationId)
    {
        await gate.PreparePreDispatchIntentAsync(lease, new AgentToolInvocationPreparePreDispatchIntentRequest
        {
            Intent = SampleIntent(lease, fingerprint)
        });
        await gate.BindPreDispatchReservationAsync(lease, new AgentToolInvocationBindReservationRequest
        {
            ReservationId = reservationId,
            Reservation = SampleReservation(lease, fingerprint)
        });
    }

    private static async Task PrepareThroughReadyAsync(
        DevelopmentInMemoryAgentToolInvocationGate gate,
        AgentToolInvocationLease lease,
        string fingerprint,
        string reservationId)
    {
        await gate.PreparePreDispatchIntentAsync(lease, new AgentToolInvocationPreparePreDispatchIntentRequest
        {
            Intent = SampleIntent(lease, fingerprint)
        });
        await gate.BindPreDispatchReservationAsync(lease, new AgentToolInvocationBindReservationRequest
        {
            ReservationId = reservationId,
            Reservation = SampleReservation(lease, fingerprint)
        });
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.Parse("2026-07-16T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
