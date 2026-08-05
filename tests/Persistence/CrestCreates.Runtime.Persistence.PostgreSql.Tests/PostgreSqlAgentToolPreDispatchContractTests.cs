using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
    private IAgentToolPreDispatchReconciliationStore _reconciliationStore = null!;
    private PostgreSqlAgentToolPreDispatchCleanup _cleanup = null!;

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
        _reconciliationStore = _provider.GetRequiredService<IAgentToolPreDispatchReconciliationStore>();
        _cleanup = _provider.GetRequiredService<PostgreSqlAgentToolPreDispatchCleanup>();
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
    public async Task Terminal_ReconciliationReceipt_Should_Supersede_StillPendingObservation()
    {
        var identity = new AgentToolPreDispatchIdentity(LogicalKey, "attempt-reconciliation-1");
        var observation = new AgentToolPreDispatchReconciliationObservation
        {
            Identity = identity,
            Status = AgentToolPreDispatchReconciliationStatus.StillPending,
            ReasonCode = "authority_unavailable",
            ObservedAt = DateTimeOffset.UtcNow,
            Revision = 1
        };

        (await _reconciliationStore.TryUpsertObservationAsync(observation, 0)).Should().BeTrue();
        var receipt = new AgentToolPreDispatchReconciliationReceipt
        {
            Identity = identity,
            Status = AgentToolPreDispatchReconciliationStatus.Released,
            ReasonCode = "released",
            TerminalAt = DateTimeOffset.UtcNow,
            IntegrityValue = "integrity-1"
        };

        (await _reconciliationStore.TryInsertReceiptAsync(receipt)).Should().BeTrue();
        (await _reconciliationStore.ReadObservationAsync(identity)).Should().BeNull();
        (await _reconciliationStore.ReadReceiptAsync(identity)).Should().BeEquivalentTo(receipt);

        (await _reconciliationStore.TryUpsertObservationAsync(observation with { Revision = 2 }, 0))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Cleanup_Should_Not_Delete_Aged_PreDispatchReadyAttempt()
    {
        var acquired = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));
        var lease = acquired.Lease!;
        await PrepareAndBindAsync(lease);

        await using (var connection = new NpgsqlConnection(_lease.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                update {_lease.Options.Schema}.agent_tool_invocation_pre_dispatch
                set created_at = clock_timestamp() - interval '365 days',
                    updated_at = clock_timestamp() - interval '365 days'
                where tenant_id = @tenantId and lease_id = @leaseId
                """;
            command.Parameters.AddWithValue("tenantId", LogicalKey.TenantId!);
            command.Parameters.AddWithValue("leaseId", lease.LeaseId);
            (await command.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await _cleanup.CleanupAsync();

        var state = await _gate.GetPreDispatchStateAsync(
            new AgentToolPreDispatchIdentity(LogicalKey, lease.AttemptId));
        state.State.Should().Be(AgentToolInvocationPreDispatchState.Ready);
        state.BoundReservationId.Should().Be("res-1");
    }

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
    public async Task Ready_Attempt_WithExpiredLease_Should_BlockReplacement()
    {
        var first = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));
        await PrepareAndBindAsync(first.Lease!);
        await ExpireLeaseAsync(first.Lease!);

        var retry = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));

        retry.Status.Should().Be(AgentToolInvocationAcquireStatus.InProgress);
        retry.Lease.Should().BeNull();
        (await _gate.GetPreDispatchStateAsync(
            new AgentToolPreDispatchIdentity(LogicalKey, first.Lease!.AttemptId)))
            .State.Should().Be(AgentToolInvocationPreDispatchState.Ready);
    }

    [Fact]
    public async Task Acquire_AfterBudgetDenial_Should_Create_NewAttempt()
    {
        var first = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));
        await _gate.PreparePreDispatchIntentAsync(first.Lease!, new AgentToolInvocationPreparePreDispatchIntentRequest
        {
            Intent = new AgentToolInvocationPreDispatchIntentSnapshot
            {
                FrozenLease = first.Lease!,
                InvocationFingerprint = "fp-1",
                Context = SampleAuditContext(first.Lease!.AttemptId),
                Approval = SampleApproval()
            }
        });
        var denial = await _gate.PublishBudgetDenialAsync(first.Lease!, new AgentToolInvocationPublishDenialRequest
        {
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.GovernanceDenied,
                Code = "budget_denied",
                Message = "budget_denied"
            },
            ReasonCode = "budget_denied"
        });

        var second = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));

        second.Status.Should().Be(AgentToolInvocationAcquireStatus.Acquired);
        second.Lease!.AttemptId.Should().NotBe(first.Lease!.AttemptId);
        var oldAttempt = await _gate.GetPreDispatchStateAsync(
            new AgentToolPreDispatchIdentity(LogicalKey, first.Lease.AttemptId));
        oldAttempt.State.Should().Be(AgentToolInvocationPreDispatchState.Abandoned);
        oldAttempt.AbandonedReceipt.Should().BeEquivalentTo(denial.AbandonedReceipt);
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
    public async Task PreparePreDispatchIntent_ChangedRetry_DoesNotReplaceFrozenIntent()
    {
        var acquire = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));
        var lease = acquire.Lease!;
        var original = new AgentToolInvocationPreDispatchIntentSnapshot
        {
            FrozenLease = lease,
            InvocationFingerprint = "fp-1",
            Context = SampleAuditContext(lease.AttemptId),
            Approval = SampleApproval()
        };
        await _gate.PreparePreDispatchIntentAsync(
            lease,
            new AgentToolInvocationPreparePreDispatchIntentRequest { Intent = original });

        var conflict = await _gate.PreparePreDispatchIntentAsync(
            lease,
            new AgentToolInvocationPreparePreDispatchIntentRequest
            {
                Intent = original with
                {
                    Approval = original.Approval with { ReasonCode = "changed" }
                }
            });

        conflict.State.Should().Be(AgentToolInvocationPreDispatchState.Unknown);
        var recovered = await _gate.GetPreDispatchStateAsync(
            new AgentToolPreDispatchIdentity(LogicalKey, lease.AttemptId));
        AgentToolGovernancePreDispatchComparer.Equivalent(recovered.Intent!, original)
            .Should().BeTrue();
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
    public async Task BindPreDispatchReservation_ChangedRetry_DoesNotReplaceBinding()
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
        var original = new AgentToolBudgetReservation
        {
            ReservationId = "res-1",
            AttemptId = lease.AttemptId,
            InvocationFingerprint = "fp-1",
            Category = "default",
            CostUnits = 1,
            MaxCallsPerExecution = 1,
            State = AgentToolBudgetReservationState.Reserved
        };
        await _gate.BindPreDispatchReservationAsync(lease, new AgentToolInvocationBindReservationRequest
        {
            ReservationId = original.ReservationId,
            Reservation = original
        });

        var conflict = await _gate.BindPreDispatchReservationAsync(lease, new AgentToolInvocationBindReservationRequest
        {
            ReservationId = original.ReservationId,
            Reservation = original with { CostUnits = 2 }
        });

        conflict.State.Should().Be(AgentToolInvocationPreDispatchState.Unknown);
        var retry = await _gate.BindPreDispatchReservationAsync(lease, new AgentToolInvocationBindReservationRequest
        {
            ReservationId = original.ReservationId,
            Reservation = original
        });
        retry.State.Should().Be(AgentToolInvocationPreDispatchState.Ready);
    }

    [Fact]
    public async Task BindAccepted_ChangedRetry_DoesNotReplaceReceipt()
    {
        var acquire = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));
        var lease = acquire.Lease!;
        await PrepareAndBindAsync(lease);
        var original = new AgentToolGovernancePreDispatchReceipt
        {
            Identity = new AgentToolPreDispatchIdentity(LogicalKey, lease.AttemptId),
            AuditId = "audit-original",
            AcceptedAt = DateTimeOffset.UtcNow
        };
        await _gate.BindAcceptedPreDispatchAsync(
            lease,
            new AgentToolInvocationBindPreDispatchRequest { Receipt = original });

        var conflict = await _gate.BindAcceptedPreDispatchAsync(
            lease,
            new AgentToolInvocationBindPreDispatchRequest
            {
                Receipt = original with { AuditId = "audit-changed" }
            });

        conflict.State.Should().Be(AgentToolInvocationPreDispatchState.Unknown);
        var recovered = await _gate.GetPreDispatchStateAsync(
            new AgentToolPreDispatchIdentity(LogicalKey, lease.AttemptId));
        AgentToolGovernancePreDispatchComparer.Equivalent(
                recovered.AcceptedReceipt!,
                original)
            .Should().BeTrue();
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

        // Finalize the governance record.
        var finalization = new AgentToolGovernanceFinalizationRecord
        {
            AuditId = receipt.Receipt!.AuditId,
            Context = context,
            Lease = checkpointRecord.Lease!,
            DispatchStarted = false,
            BudgetReservation = checkpointRecord.BudgetReservation!,
            AttemptState = AgentToolGovernanceAttemptFinalState.Released,
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.Succeeded,
                Code = "succeeded",
                Message = "completed"
            },
            OutcomeHash = "hash-1",
            ReasonCode = "released"
        };
        var finalizeResult = await _auditor.FinalizeAsync(finalization);
        finalizeResult.Status.Should().Be(AgentToolGovernanceFinalizationStatus.Finalized);
        finalizeResult.Record.Should().BeEquivalentTo(finalization);

        // Second finalize with same content — must be idempotent (Finalized, not NotFinalized).
        var secondFinalize = await _auditor.FinalizeAsync(finalization);
        secondFinalize.Status.Should().Be(AgentToolGovernanceFinalizationStatus.Finalized);
        secondFinalize.Record.Should().BeEquivalentTo(finalization);

        var conflicting = finalization with { ReasonCode = "changed" };
        var conflictResult = await _auditor.FinalizeAsync(conflicting);
        conflictResult.Status.Should().Be(AgentToolGovernanceFinalizationStatus.Finalized);
        conflictResult.Record.Should().BeEquivalentTo(finalization);

        // Verify the finalization state is readable.
        var state = await _auditor.GetFinalizationStateAsync(receipt.Receipt!.AuditId);
        state.Status.Should().Be(AgentToolGovernanceFinalizationStatus.Finalized);
        state.Record.Should().BeEquivalentTo(finalization);
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

    private async Task ExpireLeaseAsync(AgentToolInvocationLease lease)
    {
        await using var connection = new NpgsqlConnection(_lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            update {_lease.Options.Schema}.agent_tool_invocation_pre_dispatch
            set expires_at = clock_timestamp() - interval '1 second'
            where tenant_id = @tenantId and lease_id = @leaseId
            """;
        command.Parameters.AddWithValue("tenantId", LogicalKey.TenantId!);
        command.Parameters.AddWithValue("leaseId", lease.LeaseId);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
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
