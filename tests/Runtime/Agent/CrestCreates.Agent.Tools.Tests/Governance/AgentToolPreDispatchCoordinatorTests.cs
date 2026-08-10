using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools.Tests;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

/// <summary>
/// Slice 8.4 — live pre-dispatch coordinator. These tests pin the
/// behavior-preserving extraction of Blocks A–E (Prepare → Reserve → Bind
/// Reservation → Record Checkpoint → Bind Accepted) out of
/// <see cref="AgentToolInvoker"/> into the coordinator, including the
/// authoritative-recovery semantics of each step and the exact cancellation
/// behavior. They use a real finalizer against stub authorities so the shared
/// settlement logic is exercised, not mocked.
/// </summary>
public class AgentToolPreDispatchCoordinatorTests
{
    private static AgentToolLogicalInvocationKey SampleKey(string invocationId = "invocation-1")
        => new("tenant-1", "user-1", "agent-1", "execution-1", invocationId);

    private static AgentToolInvocationLease StubLease => new()
    {
        LeaseId = "lease-1",
        AttemptId = "attempt-1",
        FencingToken = 1,
        AcquiredAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
        ExpiresAt = DateTimeOffset.Parse("2026-08-01T00:05:00Z")
    };

    private static AgentToolGovernanceContext StubGovernance()
        => GovernanceTestData.Context(
            auditMode: AgentToolAuditMode.Required,
            category: "default");

    private static AgentToolGovernanceAuditContext StubAuditContext()
        => GovernanceTestData.AuditContext(StubGovernance());

    private static AgentToolApprovalResult StubApproval => new()
    {
        Decision = AgentToolApprovalDecision.NotRequired,
        ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable
    };

    private static AgentToolBudgetReservation StubReservation()
        => GovernanceTestData.Reservation(
            StubGovernance(),
            AgentToolBudgetReservationState.Reserved);

    private static AgentToolGovernancePreDispatchReceipt StubReceipt => new()
    {
        Identity = new AgentToolPreDispatchIdentity(SampleKey(), "attempt-1"),
        AuditId = "audit-1",
        AcceptedAt = DateTimeOffset.Parse("2026-08-01T00:01:00Z")
    };

    private static AgentToolRuntimeEntry BuildEntry()
    {
        var toolName = $"coordinator.tool.{Guid.NewGuid():N}";
        var capability = AgentToolRuntimeTestFixture.Capability("coordinator-capability");
        var tool = AgentToolRuntimeTestFixture.Tool(
            $"coordinator-tool-{Guid.NewGuid():N}",
            capability.Id,
            toolName,
            audit: AgentToolAuditMode.Required);
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);
        var snapshot = AgentToolRuntimeTestFixture.SnapshotBuilder(
            AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
            AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability),
            AgentToolRuntimeTestFixture.BuildSchemaRegistry())
            .Build();
        return snapshot.Find(tool.ToolName)!;
    }

    private static AgentToolPreDispatchCoordinationRequest StubRequest()
        => new()
        {
            Entry = BuildEntry(),
            Lease = StubLease,
            Governance = StubGovernance(),
            AuditContext = StubAuditContext(),
            Approval = StubApproval
        };

    private static (AgentToolPreDispatchCoordinator coordinator, CoordinatorHarness harness) CreateCoordinator()
    {
        var harness = new CoordinatorHarness();
        var finalizer = new AgentToolPreDispatchFinalizer(
            harness.Gate,
            harness.BudgetGate,
            harness.Auditor,
            harness.LeaseAbandoner);
        var coordinator = new AgentToolPreDispatchCoordinator(
            harness.Gate,
            harness.BudgetGate,
            harness.Auditor,
            finalizer);
        return (coordinator, harness);
    }

    [Fact]
    public async Task HappyPath_Should_ReturnAuthorization()
    {
        var (coordinator, harness) = CreateCoordinator();

        var result = await coordinator.ExecuteAsync(StubRequest());

        result.Kind.Should().Be(AgentToolPreDispatchAuthorizationKind.Authorized);
        result.Outcome.Should().BeNull();
        result.Reservation.Should().NotBeNull();
        result.Reservation!.AttemptId.Should().Be("attempt-1");
        result.Receipt.Should().NotBeNull();
        result.Receipt!.AuditId.Should().Be("audit-1");
        harness.CallLog.Should().ContainInOrder(
            "gate.prepare",
            "budget.reserve",
            "gate.bind-reservation",
            "auditor.record",
            "gate.bind-accepted");
    }

    [Fact]
    public async Task BudgetResponseLoss_Should_UseAuthoritativeRead()
    {
        var (coordinator, harness) = CreateCoordinator();
        harness.BudgetGate.ThrowOnReserve = true;
        // The authoritative read returns the already-persisted reservation.
        harness.BudgetGate.AuthoritativeState = AgentToolBudgetReadStatus.Reserved;

        var result = await coordinator.ExecuteAsync(StubRequest());

        result.Kind.Should().Be(AgentToolPreDispatchAuthorizationKind.Authorized);
        result.Reservation.Should().NotBeNull();
        result.Receipt.Should().NotBeNull();
        // One bounded recovery read, no terminal outcome.
        harness.BudgetGate.GetReservationStateCallCount.Should().Be(1);
        harness.Auditor.RecordPreDispatchCallCount.Should().Be(1);
    }

    [Fact]
    public async Task BudgetResponseLoss_NoPersistedReservation_Should_GoIndeterminate()
    {
        var (coordinator, harness) = CreateCoordinator();
        harness.BudgetGate.ThrowOnReserve = true;
        // The authoritative read confirms no reservation was persisted.
        harness.BudgetGate.AuthoritativeState = AgentToolBudgetReadStatus.Missing;

        var result = await coordinator.ExecuteAsync(StubRequest());

        result.Kind.Should().Be(AgentToolPreDispatchAuthorizationKind.Terminal);
        result.Outcome.Should().NotBeNull();
        result.Outcome!.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        // Preserved semantics: the factory hardcodes the generic indeterminate
        // code (the reasonCode is carried separately), matching the original
        // invoker behavior exactly.
        result.Outcome.Code.Should().Be("AGENT_TOOL_INVOCATION_INDETERMINATE");
        harness.Gate.MarkIndeterminateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task CheckpointUnavailable_Should_NotRetryWrite()
    {
        var (coordinator, harness) = CreateCoordinator();
        harness.Auditor.ThrowOnRecord = true;
        // The authoritative lookup also fails (provider unavailable) — the
        // coordinator must NOT retry the write; it goes Indeterminate.
        harness.Auditor.ThrowOnRead = true;

        var result = await coordinator.ExecuteAsync(StubRequest());

        result.Kind.Should().Be(AgentToolPreDispatchAuthorizationKind.Terminal);
        result.Outcome!.Kind.Should().Be(AgentToolInvocationOutcomeKind.InvocationIndeterminate);
        // Exactly one write attempt — no retry on unavailable lookup.
        harness.Auditor.RecordPreDispatchCallCount.Should().Be(1);
        harness.Gate.MarkIndeterminateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task CheckpointAuthoritativeMissing_Should_RetryWriteOnce()
    {
        var (coordinator, harness) = CreateCoordinator();
        harness.Auditor.ThrowOnRecord = true;
        // The authoritative read confirms Missing — one bounded retry of the
        // identical record is allowed.
        harness.Auditor.ReadStatus = AgentToolGovernancePreDispatchReadStatus.Missing;
        harness.Auditor.RecordAfterRecovery = true;

        var result = await coordinator.ExecuteAsync(StubRequest());

        result.Kind.Should().Be(AgentToolPreDispatchAuthorizationKind.Authorized);
        result.Receipt.Should().NotBeNull();
        harness.Auditor.RecordPreDispatchCallCount.Should().Be(2);
    }

    [Fact]
    public async Task BindResponseLoss_Should_UseGateRead()
    {
        var (coordinator, harness) = CreateCoordinator();
        harness.Gate.ThrowOnBindAccepted = true;
        // The authoritative gate read returns Accepted with the exact receipt.
        harness.Gate.AcceptedStateAfterRecovery = true;

        var result = await coordinator.ExecuteAsync(StubRequest());

        result.Kind.Should().Be(AgentToolPreDispatchAuthorizationKind.Authorized);
        result.Receipt.Should().NotBeNull();
        result.Receipt!.AuditId.Should().Be("audit-1");
        harness.Gate.GetPreDispatchStateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task BudgetDenial_Should_ReturnStableDeniedOutcome()
    {
        var (coordinator, harness) = CreateCoordinator();
        harness.BudgetGate.Denied = true;

        var result = await coordinator.ExecuteAsync(StubRequest());

        result.Kind.Should().Be(AgentToolPreDispatchAuthorizationKind.Terminal);
        result.Outcome.Should().NotBeNull();
        result.Outcome!.Kind.Should().Be(AgentToolInvocationOutcomeKind.GovernanceDenied);
        result.Outcome.Code.Should().Be("AGENT_TOOL_BUDGET_DENIED");
        // Stable Abandoned receipt published, lease abandoned.
        harness.Gate.PublishBudgetDenialCallCount.Should().Be(1);
        harness.LeaseAbandoner.AbandonCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_DuringPrepare_Should_RecoverThenRethrow()
    {
        var (coordinator, harness) = CreateCoordinator();
        using var cts = new CancellationTokenSource();
        harness.Gate.ThrowCancelledOnPrepare = true;
        // Recovery read finds nothing — fenced Indeterminate then rethrow.
        harness.Gate.CancelledRecoveryState = AgentToolInvocationPreDispatchState.Unknown;
        cts.Cancel();

        var act = () => coordinator.ExecuteAsync(StubRequest(), cts.Token).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.Gate.MarkIndeterminateCallCount.Should().Be(1);
    }

    private sealed class CoordinatorHarness
    {
        public StubGate Gate { get; }
        public StubBudgetGate BudgetGate { get; }
        public StubAuditor Auditor { get; }
        public StubLeaseAbandoner LeaseAbandoner { get; }
        public List<string> CallLog { get; }

        public CoordinatorHarness()
        {
            Gate = new StubGate();
            BudgetGate = new StubBudgetGate();
            Auditor = new StubAuditor();
            LeaseAbandoner = new StubLeaseAbandoner();
            CallLog = Gate.CallLog;
            BudgetGate.CallLog = CallLog;
            Auditor.CallLog = CallLog;
        }
    }

    private sealed class StubGate : IAgentToolInvocationGate
    {
        public List<string> CallLog { get; } = new();

        public bool ThrowOnBindAccepted { get; set; }
        public bool AcceptedStateAfterRecovery { get; set; }
        public bool ThrowCancelledOnPrepare { get; set; }
        public AgentToolInvocationPreDispatchState CancelledRecoveryState { get; set; } =
            AgentToolInvocationPreDispatchState.Unknown;
        public int MarkIndeterminateCallCount { get; private set; }
        public int PublishBudgetDenialCallCount { get; private set; }
        public int GetPreDispatchStateCallCount { get; private set; }

        private AgentToolInvocationPreDispatchState _state;

        public ValueTask<AgentToolInvocationPreDispatchResult> PreparePreDispatchIntentAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPreparePreDispatchIntentRequest request,
            CancellationToken cancellationToken = default)
        {
            CallLog.Add("gate.prepare");
            if (ThrowCancelledOnPrepare)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            _state = AgentToolInvocationPreDispatchState.Pending;
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Pending
            });
        }

        public ValueTask<AgentToolInvocationPreDispatchResult> BindPreDispatchReservationAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationBindReservationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallLog.Add("gate.bind-reservation");
            _state = AgentToolInvocationPreDispatchState.Ready;
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Ready
            });
        }

        public ValueTask<AgentToolInvocationPreDispatchResult> BindAcceptedPreDispatchAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationBindPreDispatchRequest request,
            CancellationToken cancellationToken = default)
        {
            CallLog.Add("gate.bind-accepted");
            if (ThrowOnBindAccepted)
                throw new InvalidOperationException("bind response unavailable");
            _state = AgentToolInvocationPreDispatchState.Accepted;
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Accepted,
                AcceptedReceipt = StubReceipt
            });
        }

        public ValueTask<AgentToolInvocationPreDispatchResult> GetPreDispatchStateAsync(
            AgentToolPreDispatchIdentity identity,
            CancellationToken cancellationToken = default)
        {
            GetPreDispatchStateCallCount++;
            CallLog.Add("gate.read");
            if (ThrowCancelledOnPrepare)
            {
                if (CancelledRecoveryState == AgentToolInvocationPreDispatchState.Unknown)
                    throw new InvalidOperationException("recovery read unavailable");
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = CancelledRecoveryState
                });
            }
            if (AcceptedStateAfterRecovery)
            {
                _state = AgentToolInvocationPreDispatchState.Accepted;
                return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
                {
                    State = AgentToolInvocationPreDispatchState.Accepted,
                    AcceptedReceipt = StubReceipt
                });
            }
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = _state
            });
        }

        public ValueTask<AgentToolInvocationPreDispatchResult> PublishBudgetDenialAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPublishDenialRequest request,
            CancellationToken cancellationToken = default)
        {
            PublishBudgetDenialCallCount++;
            CallLog.Add("gate.publish-denial");
            return ValueTask.FromResult(new AgentToolInvocationPreDispatchResult
            {
                State = AgentToolInvocationPreDispatchState.Abandoned,
                ReasonCode = request.ReasonCode
            });
        }

        public ValueTask MarkIndeterminateAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
        {
            MarkIndeterminateCallCount++;
            CallLog.Add("gate.mark-indeterminate");
            return ValueTask.CompletedTask;
        }

        public ValueTask<AgentToolInvocationAcquireResult> AcquireAsync(
            AgentToolInvocationAcquireRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask AbandonUnrecordedLeaseAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationLease> RenewAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<bool> TryMarkDispatchStartedAsync(
            AgentToolInvocationLease lease,
            AgentToolGovernancePreDispatchReceipt receipt,
            string reservationId,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask PrepareCompletionAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareCompletionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationCompletionResult> PublishCompletionAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationCompletionResult> GetCompletionStateAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask PrepareReleaseAsync(
            AgentToolInvocationLease lease,
            AgentToolInvocationPrepareReleaseRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationReleaseResult> PublishReleaseAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationReleaseResult> GetReleaseStateAsync(
            AgentToolInvocationLease lease,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationPreDispatchResult> ReleaseByIdentityAsync(
            AgentToolPreDispatchIdentity identity,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationPreDispatchResult> AbandonByIdentityAsync(
            AgentToolPreDispatchIdentity identity,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolPreDispatchReconciliationClaimResult> TryBeginPreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaimRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolInvocationPreDispatchResult> CompletePreDispatchReconciliationAsync(
            AgentToolPreDispatchReconciliationClaim claim,
            AgentToolPreDispatchReconciliationCompletionKind kind,
            string reasonCode,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubBudgetGate : IAgentToolBudgetGate
    {
        public List<string> CallLog { get; set; } = new();
        public bool ThrowOnReserve { get; set; }
        public bool Denied { get; set; }
        public AgentToolBudgetReadStatus AuthoritativeState { get; set; } =
            AgentToolBudgetReadStatus.Reserved;
        public int GetReservationStateCallCount { get; private set; }

        public ValueTask<AgentToolBudgetReserveResult> ReserveAsync(
            AgentToolBudgetReserveRequest request,
            CancellationToken cancellationToken = default)
        {
            CallLog.Add("budget.reserve");
            if (ThrowOnReserve)
                throw new InvalidOperationException("budget response unavailable");
            if (Denied)
            {
                return ValueTask.FromResult(new AgentToolBudgetReserveResult
                {
                    Status = AgentToolBudgetReserveStatus.Denied,
                    Reservation = null,
                    ReasonCode = "budget_denied"
                });
            }
            return ValueTask.FromResult(new AgentToolBudgetReserveResult
            {
                Status = AgentToolBudgetReserveStatus.Reserved,
                Reservation = StubReservation()
            });
        }

        public ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateAsync(
            AgentToolPreDispatchIdentity identity,
            CancellationToken cancellationToken = default)
        {
            GetReservationStateCallCount++;
            CallLog.Add("budget.read");
            if (AuthoritativeState == AgentToolBudgetReadStatus.Reserved)
            {
                return ValueTask.FromResult(new AgentToolBudgetReservationReadResult
                {
                    Status = AgentToolBudgetReadStatus.Reserved,
                    Reservation = StubReservation()
                });
            }
            return ValueTask.FromResult(new AgentToolBudgetReservationReadResult
            {
                Status = AgentToolBudgetReadStatus.Missing,
                Reservation = null
            });
        }

        public ValueTask<AgentToolBudgetReservation> FinalizeAsync(
            AgentToolBudgetFinalizeRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubAuditor : IAgentToolGovernanceAuditor
    {
        public List<string> CallLog { get; set; } = new();
        public bool ThrowOnRecord { get; set; }
        public bool ThrowOnRead { get; set; }
        public bool RecordAfterRecovery { get; set; }
        public AgentToolGovernancePreDispatchReadStatus ReadStatus { get; set; } =
            AgentToolGovernancePreDispatchReadStatus.Accepted;
        public int RecordPreDispatchCallCount { get; private set; }

        public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(
            AgentToolGovernancePreDispatchRecord record,
            CancellationToken cancellationToken = default)
        {
            RecordPreDispatchCallCount++;
            CallLog.Add("auditor.record");
            if (ThrowOnRecord && !(RecordAfterRecovery && RecordPreDispatchCallCount > 1))
                throw new InvalidOperationException("audit checkpoint write unavailable");
            return ValueTask.FromResult(new AgentToolGovernancePreDispatchWriteResult
            {
                Status = AgentToolGovernancePreDispatchWriteStatus.Accepted,
                Receipt = StubReceipt
            });
        }

        public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(
            AgentToolPreDispatchIdentity identity,
            CancellationToken cancellationToken = default)
        {
            CallLog.Add("auditor.read");
            if (ThrowOnRead)
                throw new InvalidOperationException("audit lookup unavailable");
            if (ReadStatus == AgentToolGovernancePreDispatchReadStatus.Accepted)
            {
                return ValueTask.FromResult(new AgentToolGovernancePreDispatchReadResult
                {
                    Status = AgentToolGovernancePreDispatchReadStatus.Accepted,
                    Receipt = StubReceipt,
                    Checkpoint = new AgentToolGovernancePreDispatchRecord
                    {
                        Context = StubAuditContext(),
                        Lease = StubLease,
                        Approval = StubApproval,
                        BudgetReservation = StubReservation()
                    }
                });
            }
            return ValueTask.FromResult(new AgentToolGovernancePreDispatchReadResult
            {
                Status = ReadStatus,
                Receipt = null,
                Checkpoint = null
            });
        }

        public ValueTask RecordDecisionAsync(
            AgentToolGovernanceDecisionRecord decision,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
            AgentToolGovernanceFinalizationRecord record,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(
            string auditId,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubLeaseAbandoner : IAgentToolInvocationLeaseAbandoner
    {
        public int AbandonCallCount { get; private set; }

        public ValueTask AbandonUnrecordedLeaseAsync(
            AgentToolInvocationLease lease,
            string reasonCode,
            CancellationToken cancellationToken = default)
        {
            AbandonCallCount++;
            return ValueTask.CompletedTask;
        }
    }
}
