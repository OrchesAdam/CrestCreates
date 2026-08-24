using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;

if (args.Length < 5)
{
    Console.Error.WriteLine("Usage: <connection-string> <schema> <operation-id> <application-name> <scenario>");
    Console.Error.WriteLine("Scenarios: commit-without-response | crash-after-human-task-insert");
    Console.Error.WriteLine("           | predispatch-cw04-budget-committed | predispatch-cw05-reservation-returned");
    Console.Error.WriteLine("           | predispatch-cw07-record-ambiguous | predispatch-cw08-checkpoint-committed");
    Console.Error.WriteLine("           | predispatch-cw09-receipt-obtained");
    Console.Error.WriteLine("           | reference-{draft|organization-unit|position|membership|role-assignment|rule}-{before-commit|after-commit|commit-unknown}");
    Console.Error.WriteLine("           | reference-{draft|organization-unit|position|membership|role-assignment|rule|hierarchy|all-org-surfaces}-save-and-exit");
    return 2;
}

var (connectionString, schema, operationId, applicationName, scenario) =
    (args[0], args[1], args[2], args[3], args[4]);

var options = new PostgreSqlRuntimePersistenceOptions { ConnectionString = connectionString, Schema = schema };

// Phase 9b+ pre-dispatch crash windows operate on the Agent Tool durable
// participants only; they do not create workflow/human-task rows.
if (scenario.StartsWith("predispatch-", StringComparison.Ordinal))
{
    return await PreDispatchCrashScenarios.RunAsync(options, scenario, applicationName);
}

// Phase 9b+ Agent Memory durable curation crash windows.
if (scenario.StartsWith("agent-memory-", StringComparison.Ordinal))
{
    return await AgentMemoryCrashScenarios.RunAsync(options, scenario, applicationName, operationId);
}

if (scenario.StartsWith("reference-", StringComparison.Ordinal))
    return await ReferenceDataCrashScenarios.RunAsync(options, scenario, applicationName);

using var provider = new ServiceCollection().AddCrestCreatesPostgreSqlRuntimePersistence(options).BuildServiceProvider();
var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
var receipts = provider.GetRequiredService<IWorkflowSuspensionReceiptStore>();
var coordinator = provider.GetRequiredService<IRuntimeTransactionCoordinator>();

var workflow = new WorkflowInstance
{
    Key = new RuntimeInstanceKey("crash", "workflow"),
    WorkflowPin = Pin("workflow", "approval", "Workflow"),
    Status = WorkflowInstanceStatus.Running,
    StartedAt = DateTimeOffset.UnixEpoch
};
await workflows.AddAsync(workflow);
var before = (await workflows.GetAsync(workflow.Key))!;
var task = new HumanTaskInstance
{
    Key = new RuntimeInstanceKey("crash", "task"),
    HumanTaskPin = Pin("humantask", "review", "HumanTask"),
    Status = HumanTaskInstanceStatus.Created,
    WorkflowKey = before.Key,
    WorkflowStepId = "review",
    RequiredCompletionConsumerIds = ["crest.workflow.humantask-continuation/v1"],
    CreatedAt = DateTimeOffset.UnixEpoch
};

switch (scenario)
{
    case "commit-without-response":
        await CommitWithoutResponseScenario(coordinator, receipts, tasks, workflows, before, task, operationId);
        break;
    case "crash-after-human-task-insert":
        await CrashAfterHumanTaskInsertScenario(coordinator, tasks, task);
        break;
    default:
        Console.Error.WriteLine($"Unknown scenario: {scenario}");
        return 3;
}

return 0;

static async Task CommitWithoutResponseScenario(
    IRuntimeTransactionCoordinator coordinator,
    IWorkflowSuspensionReceiptStore receipts,
    IHumanTaskInstanceStore tasks,
    IWorkflowInstanceStore workflows,
    WorkflowInstance before,
    HumanTaskInstance task,
    string operationId)
{
    var suspended = before.Snapshot();
    suspended.Status = WorkflowInstanceStatus.Suspended;
    suspended.WaitingHumanTaskKey = task.Key;
    var receipt = new WorkflowSuspensionReceipt
    {
        Scope = new RuntimeTenantScope("crash"),
        SuspensionOperationId = operationId,
        Integrity = Hash(operationId, "Integrity"),
        WorkflowKey = before.Key,
        HumanTaskKey = task.Key,
        WorkflowFromRevision = before.Revision,
        WorkflowToRevision = before.Revision + 1,
        WorkflowPin = before.WorkflowPin,
        HumanTaskPin = task.HumanTaskPin
    };
    await coordinator.ExecuteAsync(async cancellationToken =>
    {
        (await receipts.AddAsync(receipt, cancellationToken)).Status.ShouldBeAccepted();
        await tasks.AddAsync(task, cancellationToken);
        await workflows.UpdateAsync(suspended, before.Revision, cancellationToken);
    });

    Console.WriteLine($"COMMITTED {operationId}");
    Console.Out.Flush();
    await Task.Delay(TimeSpan.FromMinutes(2));
}

static async Task CrashAfterHumanTaskInsertScenario(
    IRuntimeTransactionCoordinator coordinator,
    IHumanTaskInstanceStore tasks,
    HumanTaskInstance task)
{
    await coordinator.ExecuteAsync(async cancellationToken =>
    {
        await tasks.AddAsync(task, cancellationToken);
        Console.WriteLine("HUMAN_TASK_INSERTED");
        Console.Out.Flush();
        await Task.Delay(TimeSpan.FromMinutes(2));
    });
}

static RuntimeDescriptorPin Pin(string @namespace, string id, string kind) => new()
{
    Ref = new DescriptorRef(@namespace, id, 1),
    ContractHash = Hash(id + "-contract", "Contract", kind),
    DefinitionHash = Hash(id + "-definition", "Definition", kind)
};

static CanonicalHash Hash(string value, string purpose, string? kind = null) => new()
{
    Value = value,
    Algorithm = "SHA-256",
    AlgorithmVersion = "sha256-canonical-json-v1",
    ArtifactKind = kind is null ? "Runtime" : "Descriptor",
    DescriptorKind = kind,
    Scope = "InternalFull",
    Purpose = purpose,
    ContractVersion = "canonical-hash-v1",
    CanonicalShapeVersion = "phase9b-crash-v1"
};

internal static class ReceiptResultExtensions
{
    public static void ShouldBeAccepted(this WorkflowSuspensionReceiptWriteStatus status)
    {
        if (status != WorkflowSuspensionReceiptWriteStatus.Accepted)
            throw new InvalidOperationException($"Expected accepted receipt, got '{status}'.");
    }
}
