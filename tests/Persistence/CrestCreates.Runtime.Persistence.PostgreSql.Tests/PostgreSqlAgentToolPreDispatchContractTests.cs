using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentToolPreDispatchContractTests : IAsyncLifetime
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;
    private PostgreSqlRuntimeSchemaLease _lease = null!;
    private ServiceProvider _provider = null!;
    private PostgreSqlAgentToolInvocationGate _gate = null!;
    private PostgreSqlAgentToolBudgetGate _budgetGate = null!;
    private PostgreSqlAgentToolGovernanceAuditor _auditor = null!;

    public PostgreSqlAgentToolPreDispatchContractTests(PostgreSqlRuntimeCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _lease = await _fixture.CreateSchemaLeaseAsync();
        _provider = BuildProvider(_lease.Options);
        _gate = _provider.GetRequiredService<PostgreSqlAgentToolInvocationGate>();
        _budgetGate = _provider.GetRequiredService<PostgreSqlAgentToolBudgetGate>();
        _auditor = _provider.GetRequiredService<PostgreSqlAgentToolGovernanceAuditor>();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _lease.DisposeAsync();
    }

    private static ServiceProvider BuildProvider(PostgreSqlRuntimePersistenceOptions options)
        => new ServiceCollection().AddCrestCreatesPostgreSqlRuntimePersistence(options).BuildServiceProvider();

    private static readonly AgentToolLogicalInvocationKey LogicalKey =
        new("tenant-1", "user-1", "agent-1", "exec-1", "inv-1");

    [Fact]
    public async Task Acquire_Duplicate_LogicalInvocation_Returns_InProgress()
    {
        var request = new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1");
        var first = await _gate.AcquireAsync(request);
        first.Status.Should().Be(AgentToolInvocationAcquireStatus.Acquired);

        var second = await _gate.AcquireAsync(request);
        second.Status.Should().Be(AgentToolInvocationAcquireStatus.InProgress);
    }

    [Fact]
    public async Task PreparePreDispatchIntent_Is_Idempotent_On_Retry()
    {
        var acquire = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));
        var lease = acquire.Lease!;
        var intent = new AgentToolInvocationPreparePreDispatchIntentRequest
        {
            Intent = new AgentToolInvocationPreDispatchIntentSnapshot
            {
                FrozenLease = lease,
                InvocationFingerprint = "fp-1",
                Context = SampleAuditContext(lease.AttemptId),
                Approval = SampleApproval()
            }
        };

        var first = await _gate.PreparePreDispatchIntentAsync(lease, intent);
        first.State.Should().Be(AgentToolInvocationPreDispatchState.Pending);

        var retry = await _gate.PreparePreDispatchIntentAsync(lease, intent);
        retry.State.Should().Be(AgentToolInvocationPreDispatchState.Pending);
    }

    [Fact]
    public async Task BindPreDispatchReservation_Is_Idempotent_On_Retry()
    {
        var acquire = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));
        var lease = acquire.Lease!;

        await _gate.PreparePreDispatchIntentAsync(lease, new AgentToolInvocationPreparePreDispatchIntentRequest
        {
            Intent = new AgentToolInvocationPreDispatchIntentSnapshot
            {
                FrozenLease = lease,
                InvocationFingerprint = "fp-1",
                Context = SampleAuditContext(lease.AttemptId),
                Approval = SampleApproval()
            }
        });

        var reservation = new AgentToolBudgetReservation
        {
            ReservationId = "res-1",
            AttemptId = lease.AttemptId,
            InvocationFingerprint = "fp-1",
            Category = "default",
            CostUnits = 1,
            MaxCallsPerExecution = 1,
            State = AgentToolBudgetReservationState.Reserved
        };

        var first = await _gate.BindPreDispatchReservationAsync(lease, new AgentToolInvocationBindReservationRequest
        {
            ReservationId = "res-1",
            Reservation = reservation
        });
        first.State.Should().Be(AgentToolInvocationPreDispatchState.Ready);

        var retry = await _gate.BindPreDispatchReservationAsync(lease, new AgentToolInvocationBindReservationRequest
        {
            ReservationId = "res-1",
            Reservation = reservation
        });
        retry.State.Should().Be(AgentToolInvocationPreDispatchState.Ready);
    }

    [Fact]
    public async Task Dispatch_CAS_Rejects_Mismatched_AcceptedAt()
    {
        var acquire = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));
        var lease = acquire.Lease!;

        await PrepareAndBindAsync(lease);

        var receipt = new AgentToolGovernancePreDispatchReceipt
        {
            Identity = new AgentToolPreDispatchIdentity(LogicalKey, lease.AttemptId),
            AuditId = "audit-1",
            AcceptedAt = DateTimeOffset.UtcNow
        };
        await _gate.BindAcceptedPreDispatchAsync(lease, new AgentToolInvocationBindPreDispatchRequest
        {
            Receipt = receipt
        });

        var wrongReceipt = receipt with { AcceptedAt = receipt.AcceptedAt.AddSeconds(1) };
        var dispatch = await _gate.TryMarkDispatchStartedAsync(lease, wrongReceipt, "res-1");
        dispatch.Should().BeFalse("dispatch CAS must reject a mismatched AcceptedAt");
    }

    [Fact]
    public async Task Dispatch_CAS_Rejects_Mismatched_AuditId()
    {
        var acquire = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));
        var lease = acquire.Lease!;

        await PrepareAndBindAsync(lease);

        var receipt = new AgentToolGovernancePreDispatchReceipt
        {
            Identity = new AgentToolPreDispatchIdentity(LogicalKey, lease.AttemptId),
            AuditId = "audit-1",
            AcceptedAt = DateTimeOffset.UtcNow
        };
        await _gate.BindAcceptedPreDispatchAsync(lease, new AgentToolInvocationBindPreDispatchRequest
        {
            Receipt = receipt
        });

        var wrongReceipt = receipt with { AuditId = "wrong-audit" };
        var dispatch = await _gate.TryMarkDispatchStartedAsync(lease, wrongReceipt, "res-1");
        dispatch.Should().BeFalse("dispatch CAS must reject a mismatched AuditId");
    }

    [Fact]
    public async Task Restart_Recovery_Restores_PreDispatch_State_And_Frozen_Intent()
    {
        var acquire = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));
        var lease = acquire.Lease!;

        await PrepareAndBindAsync(lease);

        var receipt = new AgentToolGovernancePreDispatchReceipt
        {
            Identity = new AgentToolPreDispatchIdentity(LogicalKey, lease.AttemptId),
            AuditId = "audit-1",
            AcceptedAt = DateTimeOffset.UtcNow
        };
        await _gate.BindAcceptedPreDispatchAsync(lease, new AgentToolInvocationBindPreDispatchRequest
        {
            Receipt = receipt
        });

        // Destroy and rebuild the gate from the same database.
        using var restarted = BuildProvider(_lease.Options);
        var rebuiltGate = restarted.GetRequiredService<PostgreSqlAgentToolInvocationGate>();

        var identity = new AgentToolPreDispatchIdentity(LogicalKey, lease.AttemptId);
        var state = await rebuiltGate.GetPreDispatchStateAsync(identity);

        state.State.Should().Be(AgentToolInvocationPreDispatchState.Accepted);
        state.AcceptedReceipt.Should().NotBeNull();
        state.AcceptedReceipt!.AuditId.Should().Be("audit-1");
        state.Intent.Should().NotBeNull("frozen Intent must be restored for recovery");
        state.Intent!.FrozenLease.AttemptId.Should().Be(lease.AttemptId);
    }

    [Fact]
    public async Task Budget_Reserve_Duplicate_Attempt_Restores_Original_Reservation()
    {
        var context = SampleBudgetContext("attempt-dup");
        var request = new AgentToolBudgetReserveRequest { Context = context };

        var first = await _budgetGate.ReserveAsync(request);
        first.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);

        // Retry the same attempt — should return the original reservation, not Denied.
        var second = await _budgetGate.ReserveAsync(request);
        second.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);
        second.Reservation!.ReservationId.Should().Be(first.Reservation!.ReservationId);
    }

    [Fact]
    public async Task Budget_Finalize_Honors_RequestedState_And_Is_Terminal_Monotonic()
    {
        var context = SampleBudgetContext("attempt-fin");
        var reserve = await _budgetGate.ReserveAsync(new AgentToolBudgetReserveRequest { Context = context });
        reserve.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);

        var finalizeRequest = new AgentToolBudgetFinalizeRequest
        {
            ReservationId = reserve.Reservation!.ReservationId,
            AttemptId = "attempt-fin",
            InvocationFingerprint = "fp-1",
            RequestedState = AgentToolBudgetReservationState.Released,
            ReasonCode = "reconciled"
        };

        var first = await _budgetGate.FinalizeAsync(finalizeRequest);
        first.State.Should().Be(AgentToolBudgetReservationState.Released);

        // Second finalize must not overwrite — terminal monotonicity.
        var second = await _budgetGate.FinalizeAsync(finalizeRequest);
        second.State.Should().Be(AgentToolBudgetReservationState.Released);
    }

    [Fact]
    public async Task Governance_Finalization_Is_Immutable()
    {
        var context = SampleAuditContext("attempt-gov-fin");

        var checkpointRecord = new AgentToolGovernancePreDispatchRecord
        {
            Context = context,
            Lease = new AgentToolInvocationLease
            {
                LeaseId = "lease-gov-fin",
                AttemptId = "attempt-gov-fin",
                FencingToken = 1,
                AcquiredAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            },
            Approval = SampleApproval(),
            BudgetReservation = new AgentToolBudgetReservation
            {
                ReservationId = "res-gov-fin",
                AttemptId = "attempt-gov-fin",
                InvocationFingerprint = "fp-1",
                Category = "default",
                CostUnits = 1,
                MaxCallsPerExecution = 1,
                State = AgentToolBudgetReservationState.Released
            }
        };

        var receipt = await _auditor.RecordPreDispatchAsync(checkpointRecord);
        receipt.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Accepted);

        // Record again — must not overwrite (ON CONFLICT DO NOTHING).
        var second = await _auditor.RecordPreDispatchAsync(checkpointRecord);
        second.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Duplicate);
    }

    private async Task PrepareAndBindAsync(AgentToolInvocationLease lease)
    {
        await _gate.PreparePreDispatchIntentAsync(lease, new AgentToolInvocationPreparePreDispatchIntentRequest
        {
            Intent = new AgentToolInvocationPreDispatchIntentSnapshot
            {
                FrozenLease = lease,
                InvocationFingerprint = "fp-1",
                Context = SampleAuditContext(lease.AttemptId),
                Approval = SampleApproval()
            }
        });

        await _gate.BindPreDispatchReservationAsync(lease, new AgentToolInvocationBindReservationRequest
        {
            ReservationId = "res-1",
            Reservation = new AgentToolBudgetReservation
            {
                ReservationId = "res-1",
                AttemptId = lease.AttemptId,
                InvocationFingerprint = "fp-1",
                Category = "default",
                CostUnits = 1,
                MaxCallsPerExecution = 1,
                State = AgentToolBudgetReservationState.Reserved
            }
        });
    }

    private static AgentToolGovernanceAuditContext SampleAuditContext(string attemptId)
    {
        return new AgentToolGovernanceAuditContext
        {
            LogicalInvocationKey = LogicalKey,
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
            LogicalInvocationKey = LogicalKey,
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

    private static AgentToolApprovalResult SampleApproval()
    {
        return new AgentToolApprovalResult
        {
            Decision = AgentToolApprovalDecision.Approved,
            ClaimState = AgentToolApprovalEvidenceClaimState.Claimed,
            EvidenceId = "evidence-1"
        };
    }
}
