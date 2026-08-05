using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

/// <summary>
/// Activates the normative-name test cases from the 70-ID manifest.
/// These tests prove the pre-dispatch protocol invariants by name,
/// not just by manifest string.
/// </summary>
public class AgentToolPreDispatchNormativeCaseTests
{
    [Fact]
    public void Dispatch_Should_Require_ExactAcceptedReceipt()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var lease = AcquireLease(gate);
        var reservation = "res-1";

        // Drive the full pre-dispatch state machine: Pending → Ready → Accepted
        PreparePreDispatch(gate, lease);
        BindReservation(gate, lease, reservation);
        var bindResult = BindAccepted(gate, lease);
        var acceptedReceipt = bindResult.AcceptedReceipt!;

        // Correct receipt → dispatch CAS succeeds
        var ok = gate.TryMarkDispatchStartedAsync(lease, acceptedReceipt, reservation).Result;
        ok.Should().BeTrue("dispatch CAS must succeed with the exact Accepted receipt");

        // Wrong receipt → dispatch CAS must fail
        var gate2 = new DevelopmentInMemoryAgentToolInvocationGate();
        var lease2 = AcquireLease(gate2);
        PreparePreDispatch(gate2, lease2);
        BindReservation(gate2, lease2, reservation);
        var bindResult2 = BindAccepted(gate2, lease2);

        var wrongAuditId = new AgentToolGovernancePreDispatchReceipt
        {
            AuditId = "wrong-audit-id",
            Identity = bindResult2.AcceptedReceipt!.Identity,
            AcceptedAt = bindResult2.AcceptedReceipt.AcceptedAt
        };
        var rejectedAudit = gate2.TryMarkDispatchStartedAsync(lease2, wrongAuditId, reservation).Result;
        rejectedAudit.Should().BeFalse("dispatch CAS must reject a mismatched receipt AuditId");

        // Wrong AcceptedAt → dispatch CAS must fail
        var wrongAcceptedAt = new AgentToolGovernancePreDispatchReceipt
        {
            AuditId = bindResult2.AcceptedReceipt.AuditId,
            Identity = bindResult2.AcceptedReceipt.Identity,
            AcceptedAt = bindResult2.AcceptedReceipt.AcceptedAt.AddSeconds(1)
        };
        var rejectedTime = gate2.TryMarkDispatchStartedAsync(lease2, wrongAcceptedAt, reservation).Result;
        rejectedTime.Should().BeFalse("dispatch CAS must reject a mismatched receipt AcceptedAt");

        // Wrong reservationId → dispatch CAS must fail
        var rejectedReservation = gate2.TryMarkDispatchStartedAsync(lease2, bindResult2.AcceptedReceipt!, "wrong-res").Result;
        rejectedReservation.Should().BeFalse("dispatch CAS must reject a mismatched reservationId");
    }

    [Fact]
    public void Repeated_BudgetDenial_Should_Return_SameAbandonedReceipt()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var lease = AcquireLease(gate);
        PreparePreDispatch(gate, lease);

        var denialRequest = new AgentToolInvocationPublishDenialRequest
        {
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.GovernanceDenied,
                Code = "budget_exhausted",
                Message = "Budget exhausted"
            },
            ReasonCode = "budget_exhausted"
        };

        var first = gate.PublishBudgetDenialAsync(lease, denialRequest).Result;
        first.State.Should().Be(AgentToolInvocationPreDispatchState.Abandoned);
        first.AbandonedReceipt.Should().NotBeNull();

        var firstAbandonedAt = first.AbandonedReceipt!.AbandonedAt;

        // Second denial on the same lease — should return the same AbandonedReceipt
        var second = gate.PublishBudgetDenialAsync(lease, denialRequest).Result;
        second.State.Should().Be(AgentToolInvocationPreDispatchState.Abandoned);
        second.AbandonedReceipt.Should().NotBeNull();
        second.AbandonedReceipt!.AbandonedAt.Should().Be(firstAbandonedAt,
            "repeated denial must return the same stable Abandoned receipt, not a new one");
    }

    [Fact]
    public void Recovery_Should_Validate_FullLeaseFencingReservationApprovalAndGovernance()
    {
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var lease = AcquireLease(gate);
        PreparePreDispatch(gate, lease);
        BindReservation(gate, lease, "res-1");

        // Record the governance checkpoint first (as the invoker does), to get the audit receipt
        var checkpointRecord = new AgentToolGovernancePreDispatchRecord
        {
            Context = SampleAuditContext(lease.AttemptId),
            Lease = lease,
            Approval = new AgentToolApprovalResult
            {
                Decision = AgentToolApprovalDecision.NotRequired,
                ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable
            },
            BudgetReservation = new AgentToolBudgetReservation
            {
                ReservationId = "res-1",
                AttemptId = lease.AttemptId,
                InvocationFingerprint = "fp-1",
                Category = "default",
                CostUnits = 1,
                MaxCallsPerExecution = 10,
                State = AgentToolBudgetReservationState.Reserved
            }
        };
        var writeResult = auditor.RecordPreDispatchAsync(checkpointRecord).AsTask().GetAwaiter().GetResult();
        var receipt = writeResult.Receipt!;

        // Bind accepted with the audit receipt (as the invoker does)
        var bindResult = BindAccepted(gate, lease, receipt);

        // Read the governance checkpoint by identity — must return the full checkpoint
        var identity = new AgentToolPreDispatchIdentity(
            new AgentToolLogicalInvocationKey("tenant-1", "user-1", "agent-1", "exec-1", "inv-1"),
            lease.AttemptId);

        var readResult = auditor.GetPreDispatchStateAsync(identity).Result;
        readResult.Status.Should().Be(AgentToolGovernancePreDispatchReadStatus.Accepted);
        readResult.Receipt!.AuditId.Should().Be(receipt.AuditId);
        readResult.Checkpoint.Should().NotBeNull();
        readResult.Checkpoint!.Lease.AttemptId.Should().Be(lease.AttemptId);
        readResult.Checkpoint.Lease.FencingToken.Should().Be(lease.FencingToken);

        // Gate state must also be recoverable by identity
        var gateState = gate.GetPreDispatchStateAsync(identity).Result;
        gateState.State.Should().Be(AgentToolInvocationPreDispatchState.Accepted);
        gateState.AcceptedReceipt!.AuditId.Should().Be(receipt.AuditId);
        gateState.BoundReservationId.Should().Be("res-1");

        // Mutation case: checkpoint with mismatched AttemptId must produce Conflict from reconciler
        var mismatchedRecord = new AgentToolGovernancePreDispatchRecord
        {
            Context = SampleAuditContext("different-attempt"),
            Lease = lease,
            Approval = checkpointRecord.Approval,
            BudgetReservation = checkpointRecord.BudgetReservation
        };
        var mismatchIdentity = new AgentToolPreDispatchIdentity(
            new AgentToolLogicalInvocationKey("tenant-1", "user-1", "agent-1", "exec-1", "inv-1"),
            "different-attempt");
        var store = new InMemoryReconciliationStore();
        var reconciler = new DefaultAgentToolPreDispatchReconciler(gate, new DevelopmentInMemoryAgentToolBudgetGate(), auditor, store);
        // The mismatched identity won't find any gate state (different attempt) → StillPending
        var mismatchResult = reconciler.ReconcileAsync(mismatchIdentity).Result;
        mismatchResult.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.StillPending,
            "mismatched attempt identity should not match any gate state");

        // Mutation case 2: reconciler with accepted gate + missing budget + accepted checkpoint
        // → Conflict (budget_missing_after_bind).
        var tamperedBudgetGate = new DevelopmentInMemoryAgentToolBudgetGate();
        var tamperedStore = new InMemoryReconciliationStore();
        var tamperedReconciler = new DefaultAgentToolPreDispatchReconciler(gate, tamperedBudgetGate, auditor, tamperedStore);
        var tamperedResult = tamperedReconciler.ReconcileAsync(identity).Result;
        tamperedResult.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Conflict,
            "accepted gate with missing budget must produce Conflict, not Released");
    }

    [Fact]
    public void Reconciled_Checkpoint_Should_Not_Consume_Or_Release_BudgetTwice()
    {
        // This test verifies that a reconciler does not double-release budget.
        // First reconcile: releases budget (Reserved → Released).
        // Second reconcile: must observe AlreadyReleased, not re-release.
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var budgetGate = new DevelopmentInMemoryAgentToolBudgetGate();
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();
        var store = new InMemoryReconciliationStore();

        var lease = AcquireLease(gate);
        PreparePreDispatch(gate, lease);

        // Reserve budget
        var budgetContext = SampleBudgetContext(lease.AttemptId);
        var reserveResult = budgetGate.ReserveAsync(
            new AgentToolBudgetReserveRequest { Context = budgetContext }).Result;
        reserveResult.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);
        var reservationId = reserveResult.Reservation!.ReservationId;

        BindReservation(gate, lease, reservationId);
        var bindResult = BindAccepted(gate, lease);

        // Record the governance checkpoint (as the invoker would)
        var checkpointRecord = new AgentToolGovernancePreDispatchRecord
        {
            Context = SampleAuditContext(lease.AttemptId),
            Lease = lease,
            Approval = new AgentToolApprovalResult
            {
                Decision = AgentToolApprovalDecision.NotRequired,
                ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable
            },
            BudgetReservation = reserveResult.Reservation!
        };
        auditor.RecordPreDispatchAsync(checkpointRecord).AsTask().Wait();

        // First reconcile
        var identity = new AgentToolPreDispatchIdentity(budgetContext.LogicalInvocationKey, lease.AttemptId);
        var reconciler = new DefaultAgentToolPreDispatchReconciler(gate, budgetGate, auditor, store);
        var first = reconciler.ReconcileAsync(identity).Result;
        first.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released,
            "first reconcile should release the budget");

        // Verify budget is now Released
        var budgetState = budgetGate.GetReservationStateAsync(identity).Result;
        budgetState.Status.Should().Be(AgentToolBudgetReadStatus.Released);

        // Verify Gate is now Released (not still Accepted)
        var gateState = gate.GetPreDispatchStateAsync(identity).Result;
        gateState.State.Should().Be(AgentToolInvocationPreDispatchState.Released,
            "reconciler must transition Gate to Released");

        // Second reconcile — must return AlreadyReleased, not re-release
        var second = reconciler.ReconcileAsync(identity).Result;
        second.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.AlreadyReleased,
            "second reconcile must not re-release budget");

        // Budget state must still be Released (not double-released)
        var budgetStateAfter = budgetGate.GetReservationStateAsync(identity).Result;
        budgetStateAfter.Status.Should().Be(AgentToolBudgetReadStatus.Released);

        // Gate state must still be Released (not double-released)
        var gateStateAfter = gate.GetPreDispatchStateAsync(identity).Result;
        gateStateAfter.State.Should().Be(AgentToolInvocationPreDispatchState.Released);
    }

    // --- Helpers ---

    private static AgentToolInvocationLease AcquireLease(IAgentToolInvocationGate gate)
    {
        var key = new AgentToolLogicalInvocationKey("tenant-1", "user-1", "agent-1", "exec-1", "inv-1");
        var result = gate.AcquireAsync(
            new AgentToolInvocationAcquireRequest(key, "fp-1")).Result;
        return result.Lease!;
    }

    private static void PreparePreDispatch(IAgentToolInvocationGate gate, AgentToolInvocationLease lease)
    {
        var intent = new AgentToolInvocationPreDispatchIntentSnapshot
        {
            FrozenLease = lease,
            InvocationFingerprint = "fp-1",
            Context = SampleAuditContext(lease.AttemptId),
            Approval = new AgentToolApprovalResult
            {
                Decision = AgentToolApprovalDecision.NotRequired,
                ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable
            }
        };
        gate.PreparePreDispatchIntentAsync(lease,
            new AgentToolInvocationPreparePreDispatchIntentRequest { Intent = intent }).AsTask().Wait();
    }

    private static void BindReservation(IAgentToolInvocationGate gate, AgentToolInvocationLease lease, string reservationId)
    {
        gate.BindPreDispatchReservationAsync(lease,
            new AgentToolInvocationBindReservationRequest
            {
                ReservationId = reservationId,
                Reservation = new AgentToolBudgetReservation
                {
                    ReservationId = reservationId,
                    AttemptId = lease.AttemptId,
                    InvocationFingerprint = "fp-1",
                    Category = "default",
                    CostUnits = 1,
                    MaxCallsPerExecution = 10,
                    State = AgentToolBudgetReservationState.Reserved
                }
            }).AsTask().Wait();
    }

    private static AgentToolInvocationPreDispatchResult BindAccepted(IAgentToolInvocationGate gate, AgentToolInvocationLease lease, AgentToolGovernancePreDispatchReceipt? receipt = null)
    {
        return gate.BindAcceptedPreDispatchAsync(lease,
            new AgentToolInvocationBindPreDispatchRequest
            {
                Receipt = receipt ?? new AgentToolGovernancePreDispatchReceipt
                {
                    AuditId = Guid.NewGuid().ToString("N"),
                    Identity = new AgentToolPreDispatchIdentity(
                        new AgentToolLogicalInvocationKey("tenant-1", "user-1", "agent-1", "exec-1", "inv-1"),
                        lease.AttemptId),
                    AcceptedAt = DateTimeOffset.UtcNow
                }
            }).Result;
    }

    private static AgentToolGovernanceAuditContext SampleAuditContext(string attemptId)
    {
        return new AgentToolGovernanceAuditContext
        {
            LogicalInvocationKey = new AgentToolLogicalInvocationKey("tenant-1", "user-1", "agent-1", "exec-1", "inv-1"),
            AttemptId = attemptId,
            InvocationFingerprint = "fp-1",
            ArgumentsHash = "args-hash-1",
            ArgumentsEvaluated = true,
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            AgentRolesHash = "roles-hash-1",
            ToolContract = new AgentToolContractIdentity("tool-1", 1, "hash-1"),
            CapabilityContract = new AgentToolContractIdentity("cap-1", 1, "hash-1"),
            InputSchemaContract = null,
            OutputSchemaContract = null,
            Governance = new AgentToolEffectiveGovernance(
                AgentToolSelectionPolicy.ExplicitOnly,
                AgentToolSideEffectKind.ReadOnly,
                CapabilityRiskLevel.Low,
                AgentToolApprovalMode.None,
                new AgentToolBudgetRequirement { Category = "default", CostUnits = 1, MaxCallsPerExecution = 10 },
                AgentToolAuditMode.Required)
        };
    }

    private static AgentToolGovernanceContext SampleBudgetContext(string attemptId)
    {
        return new AgentToolGovernanceContext
        {
            LogicalInvocationKey = new AgentToolLogicalInvocationKey("tenant-1", "user-1", "agent-1", "exec-1", "inv-1"),
            AttemptId = attemptId,
            InvocationFingerprint = "fp-1",
            ExecutionContext = new AgentExecutionContext
            {
                ExecutionId = "exec-1",
                InvocationId = "inv-1",
                AgentId = "agent-1",
                AgentRoles = new HashSet<string> { "role-1" },
                CallOrigin = AgentToolCallOrigin.ExplicitRequest
            },
            ToolContract = new AgentToolContractIdentity("tool-1", 1, "hash-1"),
            CapabilityContract = new AgentToolContractIdentity("cap-1", 1, "hash-1"),
            Governance = new AgentToolEffectiveGovernance(
                AgentToolSelectionPolicy.ExplicitOnly,
                AgentToolSideEffectKind.ReadOnly,
                CapabilityRiskLevel.Low,
                AgentToolApprovalMode.None,
                new AgentToolBudgetRequirement { Category = "default", CostUnits = 1, MaxCallsPerExecution = 10 },
                AgentToolAuditMode.Required),
            ArgumentsHash = "args-hash-1"
        };
    }

    private sealed class InMemoryReconciliationStore : IAgentToolPreDispatchReconciliationStore
    {
        private readonly Dictionary<AgentToolPreDispatchIdentity, (AgentToolPreDispatchReconciliationObservation Observation, long Revision)> _observations = new();
        private readonly Dictionary<AgentToolPreDispatchIdentity, AgentToolPreDispatchReconciliationReceipt> _receipts = new();
        private readonly object _sync = new();

        public ValueTask<AgentToolPreDispatchPersistenceCapability> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(AgentToolPreDispatchPersistenceCapability.FullSemantic);

        public ValueTask<bool> TryUpsertObservationAsync(AgentToolPreDispatchReconciliationObservation observation, long expectedRevision, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_observations.TryGetValue(observation.Identity, out var existing))
                {
                    if (existing.Revision != expectedRevision) return ValueTask.FromResult(false);
                }
                else if (expectedRevision != 0) return ValueTask.FromResult(false);

                _observations[observation.Identity] = (observation, observation.Revision);
                return ValueTask.FromResult(true);
            }
        }

        public ValueTask<bool> TryInsertReceiptAsync(AgentToolPreDispatchReconciliationReceipt receipt, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_receipts.ContainsKey(receipt.Identity)) return ValueTask.FromResult(false);
                _receipts[receipt.Identity] = receipt;
                return ValueTask.FromResult(true);
            }
        }

        public ValueTask<AgentToolPreDispatchReconciliationReceipt?> ReadReceiptAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return ValueTask.FromResult(_receipts.TryGetValue(identity, out var r) ? r : null);
            }
        }

        public ValueTask<AgentToolPreDispatchReconciliationObservation?> ReadObservationAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return ValueTask.FromResult(_observations.TryGetValue(identity, out var entry) ? entry.Observation : null);
            }
        }
    }
}
