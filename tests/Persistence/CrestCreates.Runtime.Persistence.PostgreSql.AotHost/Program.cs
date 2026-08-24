using System.Diagnostics;
using System.Text.Json.Serialization;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Core.Abstractions.Serialization;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Delivery;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability.Bootstrap;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Schema.Abstractions;
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
await RunControlPlaneReferenceDataMainlineAsync(options);

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
    var outbox = services.GetRequiredService<IOutboxDispatchStore>();
    var outboxClaims = await outbox.ClaimAsync(new OutboxClaimRequest
    {
        OwnerId = "aot-outbox-sentinel",
        BatchSize = 1,
        LeaseDuration = TimeSpan.FromMinutes(1),
        SupportedContractIds = new HashSet<string>(StringComparer.Ordinal),
        SupportedRequiredConsumerIds = new HashSet<string>(StringComparer.Ordinal)
    });
    if (outboxClaims.Count != 0 || await outbox.GetProviderUtcNowAsync() == default)
        return 8;
    Console.WriteLine("PHASE9C_POSTGRES_OUTBOX_AOT_OK");
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

// Phase 9b+ Durable Agent Memory mainline: sanitized Conversation/Task,
// Context/Block projection, formal curation through the real service, restart
// read, Recall/Source Expansion, and #56 Accountability.
await RunDurableAgentMemoryMainlineAsync(options);
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
    services.AddRuntimeDelivery();
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
    services.AddRuntimeDelivery();
    services.AddSingleton<IRuntimeStateContractContributor, AotRuntimeStateContractContributor>();
    services.AddCrestCreatesPostgreSqlRuntimePersistence(options);
    services.AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
    var provider = services.BuildServiceProvider();
    provider.GetRequiredService<IWorkflowRegistry>().Build([new SingleDescriptorProvider<WorkflowDescriptor>(workflow)]);
    provider.GetRequiredService<IHumanTaskRegistry>().Build([new SingleDescriptorProvider<HumanTaskDescriptor>(humanTask)]);
    return provider;
}

static async Task RunControlPlaneReferenceDataMainlineAsync(PostgreSqlRuntimePersistenceOptions options)
{
    await using var provider = BuildProvider(options, new WorkflowDescriptor(), new HumanTaskDescriptor());
    using var scope = provider.CreateScope();
    var services = scope.ServiceProvider;

    var draft = new DescriptorDraft
    {
        TenantId = "aot",
        DraftId = "reference-data-draft",
        DescriptorKind = DescriptorKind.Schema,
        DescriptorId = "reference-data-schema",
        Operation = DescriptorDraftOperation.Create,
        AuthorKind = DescriptorDraftAuthorKind.System,
        AuthorId = "aot",
        CreatedAt = DateTimeOffset.UnixEpoch,
        Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor
        {
            Id = "reference-data-schema",
            Name = "Reference Data Schema",
            Fields = new[] { new SchemaFieldDescriptor { Name = "Name", FieldType = "string" } }
        })
    };
    var drafts = services.GetRequiredService<IDescriptorDraftStore>();
    await drafts.SaveAsync(draft);
    if ((await drafts.GetAsync(draft.TenantId, draft.DraftId))?.DescriptorId != draft.DescriptorId)
        throw new InvalidOperationException("Reference Data Draft AOT round-trip failed.");

    var workflowTargets = new (string Name, InteractionTarget Target)[]
    {
        ("capability", new CapabilityTarget
        {
            Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "reference-data-capability", Version = 1 }
        }),
        ("human-task", new HumanTaskTarget
        {
            HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor> { Id = "reference-data-task", Version = 1 }
        }),
        ("sub-workflow", new SubWorkflowTarget
        {
            SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor> { Id = "reference-data-child-workflow", Version = 1 }
        })
    };
    foreach (var (name, target) in workflowTargets)
    {
        var workflowDraft = new DescriptorDraft
        {
            TenantId = "aot",
            DraftId = $"reference-data-workflow-{name}",
            DescriptorKind = DescriptorKind.Workflow,
            DescriptorId = $"reference-data-workflow-{name}",
            Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.System,
            AuthorId = "aot",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Payload = new WorkflowDescriptorDraftPayload(new WorkflowDescriptor
            {
                Id = $"reference-data-workflow-{name}",
                Name = "Reference Data Workflow",
                Steps = new[] { new WorkflowStep { Id = "step", Name = name, Target = target } }
            })
        };
        await drafts.SaveAsync(workflowDraft);
        if ((await drafts.GetAsync(workflowDraft.TenantId, workflowDraft.DraftId))?.Payload is not WorkflowDescriptorDraftPayload)
            throw new InvalidOperationException($"Reference Data Workflow {name} AOT round-trip failed.");
        Console.WriteLine(name switch
        {
            "capability" => "CRESTCREATES_DURABLE_CONTROL_PLANE_WORKFLOW_CAPABILITY_OK",
            "human-task" => "CRESTCREATES_DURABLE_CONTROL_PLANE_WORKFLOW_HUMAN_TASK_OK",
            "sub-workflow" => "CRESTCREATES_DURABLE_CONTROL_PLANE_WORKFLOW_SUBWORKFLOW_OK",
            _ => throw new InvalidOperationException($"Unknown workflow target marker '{name}'.")
        });
    }

    var unit = new OrganizationUnit
    {
        Id = "reference-data-unit",
        TenantId = "aot",
        Name = "Reference Data Unit",
        CreatedAt = DateTimeOffset.UnixEpoch
    };
    var organizations = services.GetRequiredService<IOrganizationStore>();
    await organizations.SaveOrganizationUnitAsync(unit);
    if ((await organizations.GetOrganizationUnitByIdAsync(unit.Id, unit.TenantId))?.Name != unit.Name)
        throw new InvalidOperationException("Reference Data Organization AOT round-trip failed.");

    var child = new OrganizationUnit
    {
        Id = "reference-data-child-unit",
        TenantId = "aot",
        Name = "Reference Data Child Unit",
        ParentId = unit.Id,
        CreatedAt = DateTimeOffset.UnixEpoch.AddTicks(1)
    };
    var position = new Position
    {
        Id = "reference-data-position",
        TenantId = "aot",
        Name = "Reference Data Position",
        CreatedAt = DateTimeOffset.UnixEpoch
    };
    var membership = new UserOrganizationMembership
    {
        Id = "reference-data-membership",
        TenantId = "aot",
        UserId = "reference-data-user",
        OrganizationUnitId = child.Id,
        PositionId = position.Id,
        IsPrimary = true,
        CreatedAt = DateTimeOffset.UnixEpoch
    };
    var role = new UserOrganizationRoleAssignment
    {
        Id = "reference-data-role-assignment",
        TenantId = "aot",
        UserId = membership.UserId,
        RoleId = "reference-data-role",
        OrganizationUnitId = child.Id,
        CreatedAt = DateTimeOffset.UnixEpoch
    };
    await organizations.SaveOrganizationUnitAsync(child);
    await organizations.SavePositionAsync(position);
    await organizations.SaveMembershipAsync(membership);
    await organizations.SaveRoleAssignmentAsync(role);
    if ((await organizations.GetPositionByIdAsync(position.Id, position.TenantId))?.Id != position.Id
        || !(await organizations.GetMembershipsByUserAsync(membership.UserId, membership.TenantId)).Any()
        || !(await organizations.GetRoleAssignmentsByUserAsync(role.UserId, role.TenantId)).Any())
        throw new InvalidOperationException("Reference Data Organization entity AOT round-trip failed.");

    var hierarchy = new DefaultOrganizationHierarchyService(organizations);
    if (!(await hierarchy.GetDescendantsAsync(unit.Id, unit.TenantId)).Any(value => value.Id == child.Id))
        throw new InvalidOperationException("Reference Data Organization hierarchy AOT projection failed.");
    var identity = await new DefaultOrganizationIdentityService(organizations)
        .GetContextAsync(membership.UserId, membership.TenantId);
    if (identity.PrimaryOrganizationUnitId != child.Id
        || !identity.PositionIds.Contains(position.Id, StringComparer.Ordinal)
        || !identity.RoleIds.Contains(role.RoleId, StringComparer.Ordinal))
        throw new InvalidOperationException("Reference Data Organization identity AOT projection failed.");
    Console.WriteLine("CRESTCREATES_DURABLE_REFERENCE_ORGANIZATION_OK");

    var rules = services.GetRequiredService<IDataPermissionScopeRuleStore>();
    await rules.SaveRuleAsync(new DataPermissionScopeRule
    {
        Resource = "reference-data",
        Action = "read",
        Permission = "view",
        TenantId = "aot",
        ScopeKind = DataPermissionScopeKind.Self
    });
    if (await rules.GetScopeKindAsync("reference-data", "read", "view", "aot") != DataPermissionScopeKind.Self)
        throw new InvalidOperationException("Reference Data Rule AOT round-trip failed.");

    await rules.SaveRuleAsync(new DataPermissionScopeRule
    {
        Resource = "reference-data-fallback",
        Action = "read",
        Permission = "view",
        ScopeKind = DataPermissionScopeKind.All
    });
    if (await rules.GetScopeKindAsync("reference-data-fallback", "read", "view", "aot") != DataPermissionScopeKind.All)
        throw new InvalidOperationException("Reference Data Rule global fallback AOT projection failed.");
    Console.WriteLine("CRESTCREATES_DURABLE_REFERENCE_DATA_PERMISSION_OK");

    await using var freshProvider = BuildProvider(
        options,
        new WorkflowDescriptor { Id = "reference-data-restart-workflow", Name = "Restart", Version = 1 },
        new HumanTaskDescriptor { Id = "reference-data-restart-task", Name = "Restart", Version = 1 });
    using var freshScope = freshProvider.CreateScope();
    var freshServices = freshScope.ServiceProvider;
    if ((await freshServices.GetRequiredService<IDescriptorDraftStore>()
            .GetAsync(draft.TenantId, draft.DraftId))?.DescriptorId != draft.DescriptorId
        || (await freshServices.GetRequiredService<IOrganizationStore>()
            .GetOrganizationUnitByIdAsync(unit.Id, unit.TenantId))?.Name != unit.Name
        || await freshServices.GetRequiredService<IDataPermissionScopeRuleStore>()
            .GetScopeKindAsync("reference-data-fallback", "read", "view", "aot") != DataPermissionScopeKind.All)
        throw new InvalidOperationException("Reference Data provider reconstruction failed.");

    Console.WriteLine("CRESTCREATES_DURABLE_CONTROL_PLANE_REFERENCE_DATA_OK");
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

static async Task RunDurableAgentMemoryMainlineAsync(PostgreSqlRuntimePersistenceOptions options)
{
    // First provider: full durable mainline.
    await using (var provider = BuildAgentMemoryProvider(options))
    {
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var conversations = services.GetRequiredService<IAgentConversationStore>();
        var tasks = services.GetRequiredService<IAgentTaskHistoryStore>();
        var contexts = services.GetRequiredService<IAgentCompressedContextStore>();
        var memoryStore = services.GetRequiredService<IAgentMemoryStore>();
        var promotion = services.GetRequiredService<IAgentMemoryPromotionService>();
        var hashes = services.GetRequiredService<AgentMemoryCanonicalHashProjector>();

        // 1. Conversation with accepted and rejected raw content.
        var conversation = new AgentConversationRecord
        {
            TenantId = "aot",
            ConversationId = "conversation-aot",
            Turns =
            [
                new AgentConversationTurn
                {
                    TurnId = "turn-1",
                    TenantId = "aot",
                    Role = AgentConversationRole.User,
                    Content = "accepted durable content",
                    CreatedAt = DateTimeOffset.UnixEpoch
                },
                new AgentConversationTurn
                {
                    TurnId = "turn-2",
                    TenantId = "aot",
                    Role = AgentConversationRole.Assistant,
                    Content = "   ",
                    CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(1)
                }
            ]
        };
        await conversations.SaveConversationAsync(conversation);

        // 2. Task + appended Event.
        var task = new AgentTaskRecord
        {
            TenantId = "aot",
            TaskId = "task-aot",
            Title = "aot task"
        };
        await tasks.SaveTaskAsync(task);
        await tasks.AppendEventAsync("aot", "task-aot", new AgentTaskEvent
        {
            EventId = "event-1",
            TenantId = "aot",
            TaskId = "task-aot",
            EventKind = "progress",
            Content = "appended event",
            CreatedAt = DateTimeOffset.UnixEpoch
        });

        // 3. Context with Blocks + direct Block projection.
        var context = new AgentCompressedContext
        {
            TenantId = "aot",
            ContextId = "context-aot",
            Blocks =
            [
                new AgentCompressedContextBlock
                {
                    BlockId = "block-1",
                    TenantId = "aot",
                    Content = "block content",
                    CanonicalContentHash = hashes.ComputeContentHash("aot", AgentSourceKind.ConversationTurn, "source-1", 0, 0, "block content"),
                    SourceRefs =
                    [
                        new AgentContextSourceRef
                        {
                            SourceKind = AgentSourceKind.ConversationTurn,
                            TenantId = "aot",
                            SourceId = "conversation-aot",
                            RangeStart = 0,
                            RangeEnd = 0
                        }
                    ]
                }
            ]
        };
        await contexts.CreateCompressedContextAsync(context);

        // 4. Candidate → formal Promote through the real service.
        var candidate = new AgentMemoryCandidate
        {
            TenantId = "aot",
            CandidateId = "candidate-aot",
            Kind = AgentMemoryKind.Decision,
            Content = "durable memory content",
            CanonicalContentHash = hashes.ComputeContentHash("aot", AgentSourceKind.ConversationTurn, "source-1", 0, 0, "durable memory content"),
            Confidence = AgentMemoryConfidence.High
        };
        await memoryStore.CreateCandidateAsync(candidate);
        var operation = new AgentMemoryOperationRequest
        {
            TenantId = "aot",
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = "aot",
                ActorId = "aot-host",
                ActorKind = "system",
                CorrelationId = "aot-correlation",
                InvocationSource = "system"
            },
            Reason = "aot mainline",
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = "op-aot-promote",
                OccurredAt = DateTimeOffset.UnixEpoch
            },
            Explanation = "aot mainline explanation"
        };
        var promoted = await promotion.PromoteAsync("aot", "candidate-aot", "memory-aot", operation);

        // 5. Replacement Candidate → formal Supersede through the real service.
        var replacement = new AgentMemoryCandidate
        {
            TenantId = "aot",
            CandidateId = "candidate-aot-replacement",
            Kind = AgentMemoryKind.Decision,
            Content = "replacement memory content",
            CanonicalContentHash = hashes.ComputeContentHash("aot", AgentSourceKind.ConversationTurn, "source-1", 0, 0, "replacement memory content"),
            Confidence = AgentMemoryConfidence.High
        };
        await memoryStore.CreateCandidateAsync(replacement);
        await promotion.SupersedeAsync(
            "aot", "memory-aot", "candidate-aot-replacement", "memory-aot-replacement", operation);

        // 6. Verify the reciprocal graph.
        var oldMemory = await memoryStore.GetMemoryAsync("aot", "memory-aot");
        var newMemory = await memoryStore.GetMemoryAsync("aot", "memory-aot-replacement");
        if (oldMemory?.Status != AgentMemoryStatus.Superseded
            || oldMemory.SupersededByMemoryId != "memory-aot-replacement"
            || newMemory?.SupersedesMemoryId != "memory-aot"
            || newMemory?.Status != AgentMemoryStatus.Active)
        {
            throw new InvalidOperationException("AOT durable Memory graph is not reciprocal.");
        }
    }

    // Fresh provider over the same schema: restart durability.
    await using (var fresh = BuildAgentMemoryProvider(options))
    {
        using var scope = fresh.CreateScope();
        var services = scope.ServiceProvider;
        var conversations = services.GetRequiredService<IAgentConversationStore>();
        var tasks = services.GetRequiredService<IAgentTaskHistoryStore>();
        var contexts = services.GetRequiredService<IAgentCompressedContextStore>();
        var memoryStore = services.GetRequiredService<IAgentMemoryStore>();
        var retriever = services.GetRequiredService<IAgentMemoryRetriever>();
        var expander = services.GetRequiredService<IAgentContextSourceExpander>();

        // 7. Restart reads.
        var conversation = await conversations.GetConversationAsync("aot", "conversation-aot");
        if (conversation?.Turns.Count != 1 || conversation.Turns[0].Content != "accepted durable content")
            throw new InvalidOperationException("AOT Conversation restart read failed.");
        var task = await tasks.GetTaskAsync("aot", "task-aot");
        if (task?.Events.Count != 1 || task.Events[0].Content != "appended event")
            throw new InvalidOperationException("AOT Task restart read failed.");
        var context = await contexts.GetCompressedContextAsync("aot", "context-aot");
        var block = await contexts.GetCompressedContextBlockAsync("aot", "block-1");
        if (context?.Blocks.Count != 1 || block?.Content != "block content")
            throw new InvalidOperationException("AOT Context/Block restart read failed.");

        // 8. Recall through the real Retriever.
        var pack = await retriever.RecallAsync(new AgentMemoryQuery { TenantId = "aot" });
        if (!pack.Memories.Any(m => m.MemoryId == "memory-aot-replacement"))
            throw new InvalidOperationException("AOT Recall lost the active Memory.");

        // 9. Source Expansion through the real Expander.
        var expanded = await expander.ExpandAsync(new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn,
            TenantId = "aot",
            SourceId = "conversation-aot",
            RangeStart = 0,
            RangeEnd = 0
        });
        if (expanded.Status != AgentMemorySourceExpansionStatus.Expanded
            || !expanded.SanitizedContent!.Contains("accepted durable content", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("AOT Source Expansion failed after restart.");
        }

        // 10. Curation capabilities + #56 Accountability composition validator.
        var capabilities = memoryStore as IAgentMemoryStoreCapabilities;
        if (capabilities is null
            || capabilities.CurationOutcomeGuarantee != AgentMemoryCurationOutcomeGuarantee.ConfirmedAtomic
            || memoryStore is not IAgentMemoryConditionalCurationStore)
        {
            throw new InvalidOperationException("AOT durable Memory capabilities are not ConfirmedAtomic.");
        }
        if (services.GetService<IAgentMemoryFormalCurationMarker>() is null)
            throw new InvalidOperationException("AOT formal curation marker is missing.");

        // #56: the committed Promote must have produced a durable curation
        // Accountability fact in the Runtime audit table.
        var factCount = await CountAuditFactsAsync(options, "agent-memory.promote");
        if (factCount != 1)
            throw new InvalidOperationException($"AOT durable Accountability fact count is {factCount}, expected 1.");
    }

    Console.WriteLine("CRESTCREATES_DURABLE_AGENT_MEMORY_OK");
}

static async Task<long> CountAuditFactsAsync(PostgreSqlRuntimePersistenceOptions options, string actionKind)
{
    await using var connection = new NpgsqlConnection(options.ConnectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
        $"select count(*) from \"{options.Schema}\".runtime_audit_envelopes where sink_id = @sink and envelope_json -> 'action' ->> 'kind' = @kind;",
        connection);
    command.Parameters.AddWithValue("sink", "postgresql-runtime-audit");
    command.Parameters.AddWithValue("kind", actionKind);
    return (long)(await command.ExecuteScalarAsync())!;
}

static ServiceProvider BuildAgentMemoryProvider(PostgreSqlRuntimePersistenceOptions options)
    => new ServiceCollection()
        .AddSingleton<ICanonicalHashComputer>(new AotHashComputer())
        .AddAgentMemoryRuntime()
        .AddCrestCreatesPostgreSqlRuntimePersistence(options)
        .AddCrestCreatesPostgreSqlAgentMemoryPersistence()
        .AddAccountability()
        .AddAgentMemoryAccountability()
        .BuildServiceProvider();


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

sealed class AotHashComputer : ICanonicalHashComputer
{
    public CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope)
        => Hash(descriptor.GetType().Name + "-contract");

    public CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope)
        => Hash(descriptor.GetType().Name + "-definition");

    public CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection)
    {
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
            projection.WriteCanonicalJson(writer);
        return Hash(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(stream.ToArray())).ToLowerInvariant());
    }

    private static CanonicalHash Hash(string value)
        => new()
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "AgentMemoryAot",
            Scope = "InternalFull",
            Purpose = "Aot",
            ContractVersion = "memory-hash-v1",
            CanonicalShapeVersion = "aot-v1"
        };
}
