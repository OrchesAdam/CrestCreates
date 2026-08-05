using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker;

/// <summary>
/// Phase 9b+ pre-dispatch crash scenarios. Each scenario performs the durable
/// pre-dispatch writes for a specific crash window, prints the required
/// sentinel (including the AttemptId the fresh-provider test needs to build the
/// recovery identity), and then waits so the parent test can kill the process
/// while the durable state is committed.
/// </summary>
internal static class PreDispatchCrashScenarios
{
    public static async Task<int> RunAsync(
        PostgreSqlRuntimePersistenceOptions options,
        string scenario,
        string applicationName)
    {
        var key = new AgentToolLogicalInvocationKey("crash", "user", "agent", "exec", "predispatch");
        var fp = "fp-crash";

        var governance = new AgentToolEffectiveGovernance(
            AgentToolSelectionPolicy.ExplicitOnly,
            AgentToolSideEffectKind.ReadOnly,
            CapabilityRiskLevel.Low,
            AgentToolApprovalMode.None,
            new AgentToolBudgetRequirement { Category = "default", CostUnits = 1, MaxCallsPerExecution = 10 },
            AgentToolAuditMode.Required);

        var executionContext = new AgentExecutionContext
        {
            ExecutionId = "exec-crash",
            InvocationId = "inv-crash",
            AgentId = "agent-crash",
            AgentRoles = new HashSet<string> { "role-1" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest
        };

        using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(options)
            .BuildServiceProvider();
        var gate = provider.GetRequiredService<IAgentToolInvocationGate>();
        var budget = provider.GetRequiredService<IAgentToolBudgetGate>();
        var auditor = provider.GetRequiredService<IAgentToolGovernanceAuditor>();

        var acquired = await gate.AcquireAsync(new AgentToolInvocationAcquireRequest(key, fp));
        if (acquired.Status != AgentToolInvocationAcquireStatus.Acquired || acquired.Lease is null)
            throw new InvalidOperationException($"Acquire failed: {acquired.Status}");
        var lease = acquired.Lease;

        var auditContext = new AgentToolGovernanceAuditContext
        {
            LogicalInvocationKey = key,
            AttemptId = lease.AttemptId,
            InvocationFingerprint = fp,
            ArgumentsHash = "args-crash",
            ArgumentsEvaluated = true,
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            AgentRolesHash = "roles-crash",
            ToolContract = new AgentToolContractIdentity("tool-crash", 1, "tool-hash"),
            CapabilityContract = new AgentToolContractIdentity("cap-crash", 1, "cap-hash"),
            Governance = governance
        };

        var budgetContext = new AgentToolGovernanceContext
        {
            LogicalInvocationKey = key,
            AttemptId = lease.AttemptId,
            InvocationFingerprint = fp,
            ArgumentsHash = "args-crash",
            ArgumentsEvaluated = true,
            ExecutionContext = executionContext,
            ToolContract = new AgentToolContractIdentity("tool-crash", 1, "tool-hash"),
            CapabilityContract = new AgentToolContractIdentity("cap-crash", 1, "cap-hash"),
            Governance = governance
        };

        var approval = new AgentToolApprovalResult
        {
            Decision = AgentToolApprovalDecision.NotRequired,
            ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable,
            EvidenceId = null,
            ApproverReference = "approver-crash",
            ReasonCode = "reason-crash"
        };

        await gate.PreparePreDispatchIntentAsync(lease, new AgentToolInvocationPreparePreDispatchIntentRequest
        {
            Intent = new AgentToolInvocationPreDispatchIntentSnapshot
            {
                FrozenLease = lease,
                InvocationFingerprint = fp,
                Context = auditContext,
                Approval = approval
            }
        });

        var reserveResult = await budget.ReserveAsync(new AgentToolBudgetReserveRequest { Context = budgetContext });
        if (reserveResult.Status != AgentToolBudgetReserveStatus.Reserved || reserveResult.Reservation is null)
            throw new InvalidOperationException($"Budget reserve failed: {reserveResult.Status}");

        // CW04/CW05: Reserve committed (Pending + Reserved budget), the response was
        // lost before the invoker saw it. The gate has not bound the reservation and
        // no checkpoint exists.
        if (scenario is "predispatch-cw04-budget-committed" or "predispatch-cw05-reservation-returned")
        {
            await EmitAndWaitAsync(
                scenario == "predispatch-cw04-budget-committed"
                    ? "PREDISPATCH_CW04_BUDGET_COMMITTED"
                    : "PREDISPATCH_CW05_RESERVATION_RETURNED",
                lease.AttemptId);
            return 0;
        }

        await gate.BindPreDispatchReservationAsync(lease, new AgentToolInvocationBindReservationRequest
        {
            ReservationId = reserveResult.Reservation.ReservationId,
            Reservation = reserveResult.Reservation
        });

        // CW07: reservation bound (Ready), crash before Record — checkpoint is
        // authoritatively Missing on recovery.
        if (scenario is "predispatch-cw07-record-ambiguous")
        {
            await EmitAndWaitAsync("PREDISPATCH_CW07_RECORD_AMBIGUOUS", lease.AttemptId);
            return 0;
        }

        var record = new AgentToolGovernancePreDispatchRecord
        {
            Context = auditContext,
            Lease = lease,
            Approval = approval,
            BudgetReservation = reserveResult.Reservation
        };
        var writeResult = await auditor.RecordPreDispatchAsync(record);
        if (writeResult.Status != AgentToolGovernancePreDispatchWriteStatus.Accepted || writeResult.Receipt is null)
            throw new InvalidOperationException($"RecordPreDispatch failed: {writeResult.Status}");

        // CW08/CW09: checkpoint committed (Ready + Accepted), the receipt was
        // obtained but the gate was never bound to Accepted.
        await EmitAndWaitAsync(
            scenario == "predispatch-cw08-checkpoint-committed"
                ? "PREDISPATCH_CW08_CHECKPOINT_COMMITTED"
                : "PREDISPATCH_CW09_RECEIPT_OBTAINED",
            lease.AttemptId);
        return 0;
    }

    private static async Task EmitAndWaitAsync(string sentinel, string attemptId)
    {
        Console.WriteLine($"{sentinel} {attemptId}");
        Console.Out.Flush();
        // Simulate the crash window: the parent test reads the sentinel and kills
        // this process tree while the durable state remains committed.
        await Task.Delay(TimeSpan.FromMinutes(5));
    }
}
