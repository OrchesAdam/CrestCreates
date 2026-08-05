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
    public async Task PostgreSql_Budget_Should_Enforce_MaxCallsPerExecution()
    {
        // Capacity is shared per capacity key (tenant|user|agent|execution|tool|category).
        // Three attempts against a capacity of 2 must leave exactly 2 occupied.
        var first = await _budgetGate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = SampleBudgetContext("attempt-cap-1", maxCallsPerExecution: 2)
        });
        var second = await _budgetGate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = SampleBudgetContext("attempt-cap-2", maxCallsPerExecution: 2)
        });

        first.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);
        second.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);

        var third = await _budgetGate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = SampleBudgetContext("attempt-cap-3", maxCallsPerExecution: 2)
        });

        third.Status.Should().Be(AgentToolBudgetReserveStatus.Denied);
        third.ReasonCode.Should().Be("budget_capacity_exceeded");

        // The same attempt retry is still idempotent even at capacity.
        var retry = await _budgetGate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = SampleBudgetContext("attempt-cap-1", maxCallsPerExecution: 2)
        });
        retry.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);
        retry.Reservation!.ReservationId.Should().Be(first.Reservation!.ReservationId);
    }

    [Fact]
    public async Task PostgreSql_Budget_Should_Reject_LogicalInvocationConflict()
    {
        var first = await _budgetGate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = SampleBudgetContext("attempt-conflict-1")
        });
        first.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);

        // Same LogicalInvocationKey but a different tool contract — must be rejected
        // before any capacity accounting happens.
        var second = await _budgetGate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = SampleBudgetContext(
                "attempt-conflict-2",
                toolContract: new AgentToolContractIdentity("tool-2", 1, "hash-2"))
        });

        second.Status.Should().Be(AgentToolBudgetReserveStatus.Denied);
        second.ReasonCode.Should().Be("budget_logical_invocation_conflict");
    }

    [Fact]
    public async Task PostgreSql_Budget_Should_Reject_AfterCommittedReservation()
    {
        var first = await _budgetGate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = SampleBudgetContext("attempt-committed-1")
        });
        first.Status.Should().Be(AgentToolBudgetReserveStatus.Reserved);

        var finalize = await _budgetGate.FinalizeAsync(new AgentToolBudgetFinalizeRequest
        {
            ReservationId = first.Reservation!.ReservationId,
            AttemptId = "attempt-committed-1",
            InvocationFingerprint = "fp-1",
            RequestedState = AgentToolBudgetReservationState.Committed,
            ReasonCode = "dispatched"
        });
        finalize.State.Should().Be(AgentToolBudgetReservationState.Committed);

        // A logical invocation that already reached Committed must not accept new
        // reservations for other attempts.
        var second = await _budgetGate.ReserveAsync(new AgentToolBudgetReserveRequest
        {
            Context = SampleBudgetContext("attempt-committed-2")
        });
        second.Status.Should().Be(AgentToolBudgetReserveStatus.Denied);
        second.ReasonCode.Should().Be("budget_logical_invocation_committed");
    }

    [Fact]
    public async Task Concurrent_BudgetReserve_Should_Not_ExceedCapacity()
    {
        // 12 concurrent reservations against a capacity of 10 — exactly 10 may
        // occupy the capacity key, the remaining 2 must be denied. The advisory
        // xact locks serialize the read-then-insert / count sequence.
        const int maxCalls = 10;
        const int attempts = 12;

        var tasks = Enumerable.Range(0, attempts).Select(i => _budgetGate.ReserveAsync(
            new AgentToolBudgetReserveRequest
            {
                Context = SampleBudgetContext($"attempt-par-{i}", maxCallsPerExecution: maxCalls)
            }).AsTask());

        var results = await Task.WhenAll(tasks);

        results.Count(r => r.Status == AgentToolBudgetReserveStatus.Reserved).Should().Be(maxCalls);
        results.Count(r => r.Status == AgentToolBudgetReserveStatus.Denied).Should().Be(attempts - maxCalls);
        results.Where(r => r.Status == AgentToolBudgetReserveStatus.Denied)
            .Should().OnlyContain(r => r.ReasonCode == "budget_capacity_exceeded");
    }

    [Fact]
    public async Task Completion_ResponseLoss_Should_Replay_Full_Terminal_Receipt()
    {
        var (lease, _) = await DispatchStartedAsync();

        var prepared = new AgentToolInvocationPrepareCompletionRequest
        {
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.Succeeded,
                Code = "completed",
                Message = "tool finished"
            },
            AuditId = "audit-completion-1",
            BudgetReservationId = "res-1",
            ReasonCode = "completed_normally"
        };
        await _gate.PrepareCompletionAsync(lease, prepared);

        var first = await _gate.PublishCompletionAsync(lease);
        first.State.Should().Be(AgentToolInvocationCompletionState.Completed);
        first.Outcome.Should().BeEquivalentTo(prepared.Outcome);
        first.PreparedAt.Should().NotBeNull();
        first.AuditId.Should().Be("audit-completion-1");
        first.BudgetReservationId.Should().Be("res-1");
        first.ReasonCode.Should().Be("completed_normally");

        // Response loss: second publish replays the original terminal receipt.
        var second = await _gate.PublishCompletionAsync(lease);
        second.State.Should().Be(AgentToolInvocationCompletionState.Completed);
        second.Outcome.Should().BeEquivalentTo(prepared.Outcome);
        second.AuditId.Should().Be("audit-completion-1");
        second.BudgetReservationId.Should().Be("res-1");
        second.ReasonCode.Should().Be("completed_normally");

        // Get returns the complete original receipt.
        var get = await _gate.GetCompletionStateAsync(lease);
        get.State.Should().Be(AgentToolInvocationCompletionState.Completed);
        get.Outcome.Should().BeEquivalentTo(prepared.Outcome);
        get.AuditId.Should().Be("audit-completion-1");
        get.BudgetReservationId.Should().Be("res-1");
        get.ReasonCode.Should().Be("completed_normally");
    }

    [Fact]
    public async Task Completion_Prepare_ChangedRequest_Should_Conflict()
    {
        var (lease, _) = await DispatchStartedAsync();

        var prepared = new AgentToolInvocationPrepareCompletionRequest
        {
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.Succeeded,
                Code = "completed",
                Message = "tool finished"
            },
            AuditId = "audit-completion-2",
            BudgetReservationId = "res-1",
            ReasonCode = "completed_normally"
        };
        await _gate.PrepareCompletionAsync(lease, prepared);

        // Same complete request → idempotent.
        await _gate.PrepareCompletionAsync(lease, prepared);

        // Changed request → conflict.
        var changed = prepared with
        {
            ReasonCode = "changed"
        };
        var conflict = async () => await _gate.PrepareCompletionAsync(lease, changed);
        await conflict.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Release_ResponseLoss_Should_Replay_Full_Terminal_Receipt()
    {
        var (lease, _) = await DispatchStartedAsync();

        var prepared = new AgentToolInvocationPrepareReleaseRequest
        {
            AuditId = "audit-release-1",
            BudgetReservationId = "res-1",
            ReasonCode = "reconciled_no_dispatch"
        };
        await _gate.PrepareReleaseAsync(lease, prepared);

        var first = await _gate.PublishReleaseAsync(lease);
        first.State.Should().Be(AgentToolInvocationReleaseState.Released);
        first.PreparedAt.Should().NotBeNull();
        first.AuditId.Should().Be("audit-release-1");
        first.BudgetReservationId.Should().Be("res-1");
        first.ReasonCode.Should().Be("reconciled_no_dispatch");

        // Response loss: second publish replays the original terminal receipt.
        var second = await _gate.PublishReleaseAsync(lease);
        second.State.Should().Be(AgentToolInvocationReleaseState.Released);
        second.AuditId.Should().Be("audit-release-1");
        second.BudgetReservationId.Should().Be("res-1");
        second.ReasonCode.Should().Be("reconciled_no_dispatch");

        // Get returns the complete original receipt.
        var get = await _gate.GetReleaseStateAsync(lease);
        get.State.Should().Be(AgentToolInvocationReleaseState.Released);
        get.AuditId.Should().Be("audit-release-1");
        get.BudgetReservationId.Should().Be("res-1");
        get.ReasonCode.Should().Be("reconciled_no_dispatch");
    }

    [Fact]
    public async Task Release_Prepare_ChangedRequest_Should_Conflict()
    {
        var (lease, _) = await DispatchStartedAsync();

        var prepared = new AgentToolInvocationPrepareReleaseRequest
        {
            AuditId = "audit-release-2",
            BudgetReservationId = "res-1",
            ReasonCode = "reconciled_no_dispatch"
        };
        await _gate.PrepareReleaseAsync(lease, prepared);

        // Same complete request → idempotent.
        await _gate.PrepareReleaseAsync(lease, prepared);

        // Changed request → conflict.
        var changed = prepared with
        {
            ReasonCode = "changed"
        };
        var conflict = async () => await _gate.PrepareReleaseAsync(lease, changed);
        await conflict.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task<(AgentToolInvocationLease Lease, AgentToolGovernancePreDispatchReceipt Receipt)> DispatchStartedAsync()
    {
        var acquire = await _gate.AcquireAsync(new AgentToolInvocationAcquireRequest(LogicalKey, "fp-1"));
        var lease = acquire.Lease!;
        await PrepareAndBindAsync(lease);

        var receipt = new AgentToolGovernancePreDispatchReceipt
        {
            Identity = new AgentToolPreDispatchIdentity(LogicalKey, lease.AttemptId),
            AuditId = "audit-dispatch-start",
            AcceptedAt = DateTimeOffset.UtcNow
        };
        await _gate.BindAcceptedPreDispatchAsync(lease, new AgentToolInvocationBindPreDispatchRequest
        {
            Receipt = receipt
        });

        var dispatch = await _gate.TryMarkDispatchStartedAsync(lease, receipt, "res-1");
        dispatch.Should().BeTrue();
        return (lease, receipt);
    }

    // B09 (real owner): retention at exactly the minimum window — the record
    // remains queryable through the window boundary; strictly older terminal
    // observations are cleaned.
    [Fact]
    public async Task PostgreSql_B09_Retention_AtMinimumWindow_Should_RemainQueryable()
    {
        var now = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        var coordinator = _provider.GetRequiredService<PostgreSqlRuntimeTransactionCoordinator>();
        var cleanup = new PostgreSqlAgentToolPreDispatchCleanup(
            coordinator, _lease.Options, clock);

        var identity = new AgentToolPreDispatchIdentity(LogicalKey, "attempt-b09");
        var boundary = now - _lease.Options.ReconciliationObservationRetention;

        // Exactly at the minimum window boundary — must survive cleanup.
        (await _reconciliationStore.TryUpsertObservationAsync(
            new AgentToolPreDispatchReconciliationObservation
            {
                Identity = identity,
                Status = AgentToolPreDispatchReconciliationStatus.Released,
                ReasonCode = "released",
                ObservedAt = boundary,
                Revision = 1
            }, 0)).Should().BeTrue();

        // Strictly beyond the window — must be cleaned.
        var beyondIdentity = new AgentToolPreDispatchIdentity(LogicalKey, "attempt-b09-beyond");
        (await _reconciliationStore.TryUpsertObservationAsync(
            new AgentToolPreDispatchReconciliationObservation
            {
                Identity = beyondIdentity,
                Status = AgentToolPreDispatchReconciliationStatus.Released,
                ReasonCode = "released",
                ObservedAt = boundary.AddSeconds(-1),
                Revision = 1
            }, 0)).Should().BeTrue();

        await cleanup.CleanupAsync();

        (await _reconciliationStore.ReadObservationAsync(identity)).Should().NotBeNull();
        (await _reconciliationStore.ReadObservationAsync(beyondIdentity)).Should().BeNull();
    }

    // F18 (real owner): cleanup races live reconciliation — StillPending is
    // mutable retry state and must never be age-deleted, even far beyond the window.
    [Fact]
    public async Task PostgreSql_F18_Cleanup_Should_Not_Remove_LiveReconciliationState()
    {
        var now = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        var coordinator = _provider.GetRequiredService<PostgreSqlRuntimeTransactionCoordinator>();
        var cleanup = new PostgreSqlAgentToolPreDispatchCleanup(
            coordinator, _lease.Options, clock);

        var identity = new AgentToolPreDispatchIdentity(LogicalKey, "attempt-f18");
        (await _reconciliationStore.TryUpsertObservationAsync(
            new AgentToolPreDispatchReconciliationObservation
            {
                Identity = identity,
                Status = AgentToolPreDispatchReconciliationStatus.StillPending,
                ReasonCode = "authority_unavailable",
                ObservedAt = now - TimeSpan.FromDays(400),
                Revision = 1
            }, 0)).Should().BeTrue();

        await cleanup.CleanupAsync();

        var read = await _reconciliationStore.ReadObservationAsync(identity);
        read.Should().NotBeNull("StillPending is mutable retry state and must survive cleanup");
        read!.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.StillPending);
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
        var state = await _auditor.GetFinalizationStateAsync(receipt.Receipt!.AuditId, receipt.Receipt!.Identity.LogicalInvocationKey.TenantId);
        state.Status.Should().Be(AgentToolGovernanceFinalizationStatus.Finalized);
        state.Record.Should().BeEquivalentTo(finalization);
    }

    [Fact]
    public async Task Decision_Retry_Is_Idempotent_With_Stable_Identity()
    {
        var decision = new AgentToolGovernanceDecisionRecord
        {
            Context = SampleAuditContext("attempt-decision-1"),
            Decision = AgentToolGovernanceDecisionState.Denied,
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.CapabilityFailure,
                Code = "denied",
                Message = "rejected"
            },
            ReasonCode = "policy_denied"
        };

        // First record.
        await _auditor.RecordDecisionAsync(decision);

        // Response-loss retry with identical content must be idempotent (no throw, no duplicate).
        await _auditor.RecordDecisionAsync(decision);

        // A different decision for the same attempt identity must conflict.
        var conflicting = decision with { ReasonCode = "changed" };
        var conflict = async () => await _auditor.RecordDecisionAsync(conflicting);
        await conflict.Should().ThrowAsync<InvalidOperationException>();

        // Exactly one row exists for the stable decision identity.
        await using var connection = new NpgsqlConnection(_lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select count(*)
            from {_lease.Options.Schema}.agent_tool_governance_decisions
            where tenant_id = @tenantId
              and attempt_id = @attemptId
              and decision_state = @decisionState
            """;
        command.Parameters.AddWithValue("tenantId", LogicalKey.TenantId!);
        command.Parameters.AddWithValue("attemptId", "attempt-decision-1");
        command.Parameters.AddWithValue("decisionState", (int)AgentToolGovernanceDecisionState.Denied);
        (await command.ExecuteScalarAsync()).Should().Be(1L);
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

    private static AgentToolGovernanceContext SampleBudgetContext(
        string attemptId,
        int? maxCallsPerExecution = 10,
        AgentToolContractIdentity? toolContract = null,
        string? fingerprint = null)
    {
        return new AgentToolGovernanceContext
        {
            LogicalInvocationKey = LogicalKey,
            AttemptId = attemptId,
            InvocationFingerprint = fingerprint ?? "fp-1",
            ExecutionContext = new AgentExecutionContext
            {
                ExecutionId = "exec-1",
                InvocationId = "inv-1",
                AgentId = "agent-1",
                AgentRoles = new HashSet<string> { "role-1" },
                CallOrigin = AgentToolCallOrigin.ExplicitRequest
            },
            ToolContract = toolContract ?? new AgentToolContractIdentity("tool-1", 1, "hash-1"),
            CapabilityContract = new AgentToolContractIdentity("cap-1", 1, "hash-1"),
            Governance = new AgentToolEffectiveGovernance(
                AgentToolSelectionPolicy.ExplicitOnly,
                AgentToolSideEffectKind.ReadOnly,
                CapabilityRiskLevel.Low,
                AgentToolApprovalMode.None,
                new AgentToolBudgetRequirement { Category = "default", CostUnits = 1, MaxCallsPerExecution = maxCallsPerExecution },
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

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
