using System.Diagnostics;
using System.Text.Json.Serialization;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Core.Abstractions.Serialization;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

if (args.Length is not (2 or 3))
{
    Console.Error.WriteLine("Usage: <connection-string> <schema> [predispatch-scenario]");
    return 2;
}

var options = new PostgreSqlRuntimePersistenceOptions { ConnectionString = args[0], Schema = args[1] };

// Child mode: CrashWorker-style subprocess that performs the durable pre-dispatch
// writes for one crash window, prints the commit sentinel (with AttemptId), then
// waits to be killed while the committed state stays durable in PostgreSQL.
if (args.Length == 3)
    return await RunPreDispatchCrashChildAsync(options, args[2]);

await new PostgreSqlRuntimeMigrationRunner(options).ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });

var workflowDescriptor = new WorkflowDescriptor { Id = "approval", Name = "Approval", Version = 1 };
var humanTaskDescriptor = new HumanTaskDescriptor
{
    Id = "review",
    Name = "Review",
    Version = 1,
    Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("review-form", 1),
    AssigneeStrategy = AssigneeStrategy.SingleUser
};
var workflowKey = new RuntimeInstanceKey("aot", "workflow");
var humanTaskKey = new RuntimeInstanceKey("aot", "task");
var mutable = new MutableNestedAotState { Name = "native", Values = ["before", "suspend"] };

await using (var first = BuildProvider(options, workflowDescriptor, humanTaskDescriptor))
{
    using var scope = first.CreateScope();
    var services = scope.ServiceProvider;
    var states = services.GetRequiredService<IRuntimeStateContractRegistry>();
    var workflowPins = services.GetRequiredService<IRuntimeDescriptorPinResolver<WorkflowDescriptor>>();
    var taskPins = services.GetRequiredService<IRuntimeDescriptorPinResolver<HumanTaskDescriptor>>();
    var workflow = new WorkflowInstance
    {
        Key = workflowKey,
        WorkflowPin = workflowPins.Capture(workflowDescriptor).Pin,
        StartedAt = DateTimeOffset.UnixEpoch,
        Variables = new Dictionary<string, RuntimeStateValue>(StringComparer.Ordinal)
        {
            ["mutable"] = states.Capture(mutable)
        }
    };
    var humanTask = new HumanTaskInstance
    {
        Key = humanTaskKey,
        HumanTaskPin = taskPins.Capture(humanTaskDescriptor).Pin,
        WorkflowKey = workflowKey,
        WorkflowStepId = "review",
        Status = HumanTaskInstanceStatus.Assigned,
        Input = states.Capture(new MutableNestedAotState { Name = "input", Values = ["typed"] }),
        CreatedAt = DateTimeOffset.UnixEpoch
    };

    var workflows = services.GetRequiredService<IWorkflowInstanceStore>();
    await workflows.AddAsync(workflow);
    var before = (await workflows.GetAsync(workflowKey))!;
    var suspended = before.Snapshot();
    suspended.Status = WorkflowInstanceStatus.Suspended;
    suspended.WaitingHumanTaskKey = humanTaskKey;

    await services.GetRequiredService<WorkflowSuspensionCommitter>()
        .CommitAsync(before, suspended, humanTask, "aot-suspend", CancellationToken.None);

    var audit = new AuditEnvelope
    {
        AuditId = "aot-audit",
        OccurredAt = DateTimeOffset.UnixEpoch,
        CorrelationId = "aot",
        Actor = new AuditActor { Kind = "system", Id = "aot" },
        Action = new AuditAction { Kind = "runtime", Name = "aot" },
        Target = new AuditTarget { Kind = "workflow", Id = workflowKey.InstanceId },
        Outcome = new AuditOutcome { Status = "succeeded" },
        Integrity = RuntimeHash("aot-audit", "AuditIntegrity")
    };
    if ((await services.GetRequiredService<IAuditSink>().WriteAsync(audit)).Status != AuditSinkWriteStatus.Accepted)
        return 3;
}

await using (var fresh = BuildProvider(options, workflowDescriptor, humanTaskDescriptor))
{
    using var scope = fresh.CreateScope();
    var services = scope.ServiceProvider;
    var workflows = services.GetRequiredService<IWorkflowInstanceStore>();
    var tasks = services.GetRequiredService<IHumanTaskInstanceStore>();
    var receipts = services.GetRequiredService<IWorkflowSuspensionReceiptStore>();
    var states = services.GetRequiredService<IRuntimeStateContractRegistry>();
    var recoveredWorkflow = await workflows.GetAsync(workflowKey);
    var recoveredTask = await tasks.GetAsync(humanTaskKey);
    if (recoveredWorkflow?.Status != WorkflowInstanceStatus.Suspended
        || recoveredWorkflow.WaitingHumanTaskKey != humanTaskKey
        || recoveredTask?.WorkflowKey != workflowKey
        || (await receipts.GetAsync(new RuntimeTenantScope("aot"), "aot-suspend")) is null)
        return 4;
    Console.WriteLine("PHASE9B_POSTGRES_SUSPENSION_OK");

    var restored = states.Restore<MutableNestedAotState>(recoveredWorkflow.Variables["mutable"]);
    if (restored.Name != mutable.Name || !restored.Values.SequenceEqual(mutable.Values, StringComparer.Ordinal))
        return 5;
    Console.WriteLine("PHASE9B_POSTGRES_STATE_OK");

    var resolvedWorkflow = services.GetRequiredService<IRuntimeDescriptorPinResolver<WorkflowDescriptor>>()
        .Resolve(recoveredWorkflow.WorkflowPin);
    var resolvedTask = services.GetRequiredService<IRuntimeDescriptorPinResolver<HumanTaskDescriptor>>()
        .Resolve(recoveredTask.HumanTaskPin);
    if (!ReferenceEquals(resolvedWorkflow.Descriptor, workflowDescriptor)
        || !ReferenceEquals(resolvedTask.Descriptor, humanTaskDescriptor))
        return 6;
    Console.WriteLine("PHASE9B_POSTGRES_PIN_RECOVERY_OK");

    var duplicate = await services.GetRequiredService<IAuditSink>().WriteAsync(new AuditEnvelope
    {
        AuditId = "aot-audit",
        OccurredAt = DateTimeOffset.UnixEpoch,
        CorrelationId = "aot",
        Actor = new AuditActor { Kind = "system", Id = "aot" },
        Action = new AuditAction { Kind = "runtime", Name = "aot" },
        Target = new AuditTarget { Kind = "workflow", Id = workflowKey.InstanceId },
        Outcome = new AuditOutcome { Status = "succeeded" },
        Integrity = RuntimeHash("aot-audit", "AuditIntegrity")
    });
    if (duplicate.Status != AuditSinkWriteStatus.Duplicate)
        return 7;
    Console.WriteLine("PHASE9B_POSTGRES_AUDIT_RETRY_OK");
}

// Phase 9b+ Durable Agent Tool Pre-Dispatch Reconciliation crash scenarios:
// real CrashWorker-style subprocess commit → kill → fresh-process recovery for
// CW04/CW05/CW07/CW08/CW09, converging without any dispatcher call.
await RunPreDispatchCrashScenariosAsync(options);

return 0;

static async Task RunPreDispatchCrashScenariosAsync(PostgreSqlRuntimePersistenceOptions options)
{
    // scenario, sentinel prefix, gate state expected after fresh-provider recovery
    (string Scenario, string Sentinel, AgentToolInvocationPreDispatchState GateState)[] scenarios =
    [
        ("predispatch-cw04-budget-committed", "PREDISPATCH_CW04_BUDGET_COMMITTED", AgentToolInvocationPreDispatchState.Abandoned),
        ("predispatch-cw05-reservation-returned", "PREDISPATCH_CW05_RESERVATION_RETURNED", AgentToolInvocationPreDispatchState.Abandoned),
        ("predispatch-cw07-record-ambiguous", "PREDISPATCH_CW07_RECORD_AMBIGUOUS", AgentToolInvocationPreDispatchState.Abandoned),
        ("predispatch-cw08-checkpoint-committed", "PREDISPATCH_CW08_CHECKPOINT_COMMITTED", AgentToolInvocationPreDispatchState.Released),
        ("predispatch-cw09-receipt-obtained", "PREDISPATCH_CW09_RECEIPT_OBTAINED", AgentToolInvocationPreDispatchState.Released)
    ];

    foreach (var scenario in scenarios)
    {
        // Spawn this same (native) executable as the crash worker for the window.
        var applicationName = "aot-predispatch-" + Guid.NewGuid().ToString("N");
        var connectionBuilder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
        {
            ApplicationName = applicationName
        };
        using var child = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath unavailable"),
                Arguments = $"\"{connectionBuilder.ConnectionString}\" {options.Schema} {scenario.Scenario}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        child.Start();
        using var readyTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var stderrTask = child.StandardError.ReadToEndAsync(readyTimeout.Token);
        string? attemptId = null;
        while (await child.StandardOutput.ReadLineAsync(readyTimeout.Token) is { } line)
        {
            if (line.StartsWith(scenario.Sentinel, StringComparison.Ordinal))
            {
                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                attemptId = parts.Length == 2 ? parts[1] : null;
                break;
            }
        }
        if (attemptId is null)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException(
                $"[{scenario.Scenario}] Crash worker produced no sentinel. Stderr: {stderr}");
        }

        child.Kill(entireProcessTree: true);
        await child.WaitForExitAsync();
        await WaitForBackendExitAsync(options.ConnectionString, applicationName);

        var key = new AgentToolLogicalInvocationKey("tenant", "user", "agent", "exec", scenario.Scenario);
        var identity = new AgentToolPreDispatchIdentity(key, attemptId);

        // Fresh process recovers by identity and converges the crash window.
        await using (var fresh = BuildPreDispatchProvider(options))
        {
            using var scope = fresh.CreateScope();
            var services = scope.ServiceProvider;
            var gate = services.GetRequiredService<IAgentToolInvocationGate>();
            var budget = services.GetRequiredService<IAgentToolBudgetGate>();
            var reconciler = services.GetRequiredService<IAgentToolPreDispatchReconciler>();

            var result1 = await reconciler.ReconcileAsync(
                identity,
                cancellationToken: default,
                context: new AgentToolPreDispatchReconciliationContext
                {
                    OwnershipLost = true,
                    OwnershipEvidence = "process-tree-killed"
                });
            if (result1.Status != AgentToolPreDispatchReconciliationStatus.Released)
                throw new InvalidOperationException(
                    $"[{scenario.Scenario}] First reconcile failed: {result1.Status}");

            var postReconcileBudget = await budget.GetReservationStateAsync(identity);
            if (postReconcileBudget.Status != AgentToolBudgetReadStatus.Released)
                throw new InvalidOperationException(
                    $"[{scenario.Scenario}] Budget not released after reconcile: {postReconcileBudget.Status}");

            var postReconcileGate = await gate.GetPreDispatchStateAsync(identity);
            if (postReconcileGate.State != scenario.GateState)
                throw new InvalidOperationException(
                    $"[{scenario.Scenario}] Gate not {scenario.GateState} after reconcile: {postReconcileGate.State}");

            var result2 = await reconciler.ReconcileAsync(
                identity,
                cancellationToken: default,
                context: new AgentToolPreDispatchReconciliationContext
                {
                    OwnershipLost = true,
                    OwnershipEvidence = "process-tree-killed"
                });
            if (result2.Status != AgentToolPreDispatchReconciliationStatus.AlreadyReleased)
                throw new InvalidOperationException(
                    $"[{scenario.Scenario}] Second reconcile failed: {result2.Status}");
        }

        var crashWindow = scenario.Sentinel["PREDISPATCH_".Length..].Split('_')[0];
        Console.WriteLine($"CRESTCREATES_AGENTTOOL_PREDISPATCH_{crashWindow}_OK");
    }

    Console.WriteLine("CRESTCREATES_DURABLE_AGENT_TOOL_PREDISPATCH_OK");
}

/// <summary>
/// Performs the durable pre-dispatch writes for one crash window, prints the
/// commit sentinel (with the AttemptId the recovering process needs), and then
/// waits so the parent process can kill this subprocess mid-window.
/// </summary>
static async Task<int> RunPreDispatchCrashChildAsync(
    PostgreSqlRuntimePersistenceOptions options,
    string scenario)
{
    var key = new AgentToolLogicalInvocationKey("tenant", "user", "agent", "exec", scenario);
    var fp = "fp-aot";

    var governance = new AgentToolEffectiveGovernance(
        AgentToolSelectionPolicy.ExplicitOnly,
        AgentToolSideEffectKind.ReadOnly,
        CapabilityRiskLevel.Low,
        AgentToolApprovalMode.None,
        new AgentToolBudgetRequirement { Category = "default", CostUnits = 1, MaxCallsPerExecution = 10 },
        AgentToolAuditMode.Required);

    var executionContext = new AgentExecutionContext
    {
        ExecutionId = key.ExecutionId,
        InvocationId = key.InvocationId,
        AgentId = key.AgentId,
        AgentRoles = new HashSet<string> { "role-1" },
        CallOrigin = AgentToolCallOrigin.ExplicitRequest
    };

    await using (var provider = BuildPreDispatchProvider(options))
    {
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var gate = services.GetRequiredService<IAgentToolInvocationGate>();
        var budget = services.GetRequiredService<IAgentToolBudgetGate>();
        var auditor = services.GetRequiredService<IAgentToolGovernanceAuditor>();

        var acquired = await gate.AcquireAsync(new AgentToolInvocationAcquireRequest(key, fp));
        if (acquired.Status != AgentToolInvocationAcquireStatus.Acquired || acquired.Lease is null)
            throw new InvalidOperationException($"Acquire failed: {acquired.Status}");
        var lease = acquired.Lease;

        var auditContext = new AgentToolGovernanceAuditContext
        {
            LogicalInvocationKey = key,
            AttemptId = lease.AttemptId,
            InvocationFingerprint = fp,
            ArgumentsHash = "args-aot",
            ArgumentsEvaluated = true,
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            AgentRolesHash = "roles-aot",
            ToolContract = new AgentToolContractIdentity("tool-aot", 1, "tool-hash"),
            CapabilityContract = new AgentToolContractIdentity("cap-aot", 1, "cap-hash"),
            Governance = governance
        };

        var budgetContext = new AgentToolGovernanceContext
        {
            LogicalInvocationKey = key,
            AttemptId = lease.AttemptId,
            InvocationFingerprint = fp,
            ArgumentsHash = "args-aot",
            ArgumentsEvaluated = true,
            ExecutionContext = executionContext,
            ToolContract = new AgentToolContractIdentity("tool-aot", 1, "tool-hash"),
            CapabilityContract = new AgentToolContractIdentity("cap-aot", 1, "cap-hash"),
            Governance = governance
        };

        var approval = new AgentToolApprovalResult
        {
            Decision = AgentToolApprovalDecision.NotRequired,
            ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable,
            EvidenceId = null,
            ApproverReference = "approver-aot",
            ReasonCode = "reason-aot"
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

        // CW04/CW05: Reserve committed (Pending + Reserved budget), response lost
        // before the invoker saw it — the gate never bound the reservation.
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

        // CW07: reservation bound (Ready), crash before Record — the checkpoint is
        // authoritatively Missing on recovery.
        if (scenario == "predispatch-cw07-record-ambiguous")
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

        // CW08/CW09: checkpoint committed (Ready + Accepted), receipt obtained but
        // the gate was never bound to Accepted.
        await EmitAndWaitAsync(
            scenario == "predispatch-cw08-checkpoint-committed"
                ? "PREDISPATCH_CW08_CHECKPOINT_COMMITTED"
                : "PREDISPATCH_CW09_RECEIPT_OBTAINED",
            lease.AttemptId);
        return 0;
    }
}

static async Task EmitAndWaitAsync(string sentinel, string attemptId)
{
    Console.WriteLine($"{sentinel} {attemptId}");
    Console.Out.Flush();
    // Simulate the crash window: the parent process reads the sentinel and kills
    // this subprocess tree while the durable state remains committed.
    await Task.Delay(TimeSpan.FromMinutes(5));
}

static async Task WaitForBackendExitAsync(string connectionString, string applicationName)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
    while (DateTimeOffset.UtcNow < deadline)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from pg_stat_activity where application_name=@application;", connection);
        command.Parameters.AddWithValue("application", applicationName);
        if ((long)(await command.ExecuteScalarAsync())! == 0)
            return;
        await Task.Delay(TimeSpan.FromMilliseconds(100));
    }

    throw new TimeoutException("The crash worker PostgreSQL backend did not exit.");
}

static ServiceProvider BuildPreDispatchProvider(PostgreSqlRuntimePersistenceOptions options)
{
    var services = new ServiceCollection();
    services.AddAccountability();
    services.AddCrestAgentTools();
    services.AddCrestCreatesPostgreSqlRuntimePersistence(options);
    var provider = services.BuildServiceProvider();
    return provider;
}

static ServiceProvider BuildProvider(
    PostgreSqlRuntimePersistenceOptions options,
    WorkflowDescriptor workflow,
    HumanTaskDescriptor humanTask)
{
    var services = new ServiceCollection();
    services.AddRuntimePersistence();
    services.AddDescriptorStableHash();
    services.AddWorkflowEngine();
    services.AddHumanTaskRuntime();
    services.AddSingleton<IRuntimeStateContractContributor, AotRuntimeStateContractContributor>();
    services.AddCrestCreatesPostgreSqlRuntimePersistence(options);
    var provider = services.BuildServiceProvider();
    provider.GetRequiredService<IWorkflowRegistry>().Build([new SingleDescriptorProvider<WorkflowDescriptor>(workflow)]);
    provider.GetRequiredService<IHumanTaskRegistry>().Build([new SingleDescriptorProvider<HumanTaskDescriptor>(humanTask)]);
    return provider;
}

static CanonicalHash RuntimeHash(string value, string purpose) => new()
{
    Value = value,
    Algorithm = "SHA-256",
    AlgorithmVersion = "sha256-canonical-json-v1",
    ArtifactKind = "Runtime",
    Scope = "InternalFull",
    Purpose = purpose,
    ContractVersion = "canonical-hash-v1",
    CanonicalShapeVersion = "phase9b-aot-v1"
};

internal sealed class SingleDescriptorProvider<TDescriptor>(TDescriptor descriptor) : IDescriptorProvider<TDescriptor>
    where TDescriptor : IDescriptor
{
    public IReadOnlyList<TDescriptor> GetDescriptors() => [descriptor];
}

public sealed class MutableNestedAotState
{
    public string Name { get; init; } = string.Empty;
    public List<string> Values { get; init; } = new();
}

public sealed class AotRuntimeStateContractContributor : IRuntimeStateContractContributor
{
    public void Contribute(IRuntimeStateContractBuilder builder)
        => builder.Add(
            "aot/runtime/mutable-state/v1",
            AotRuntimeStateJsonSerializerContext.Default.MutableNestedAotState,
            AotRuntimeStateJsonSerializerContext.AotRuntimeStateJsonSerializerContextRootManifest.AllDirectRootTypes);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonContractSurface(typeof(IAotRuntimeStateJsonSurface))]
[JsonContractExplicitRoot(typeof(MutableNestedAotState))]
public sealed partial class AotRuntimeStateJsonSerializerContext : JsonSerializerContext
{
}

internal interface IAotRuntimeStateJsonSurface
{
    MutableNestedAotState State(MutableNestedAotState value);
}
