using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: <connection-string> <schema>");
    return 2;
}

var options = new PostgreSqlRuntimePersistenceOptions
{
    ConnectionString = args[0],
    Schema = args[1]
};
await new PostgreSqlRuntimeMigrationRunner(options).ApplyAsync(
    new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });

using var provider = new ServiceCollection()
    .AddCrestCreatesPostgreSqlRuntimePersistence(options)
    .BuildServiceProvider();

var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
var receipts = provider.GetRequiredService<IWorkflowSuspensionReceiptStore>();
var coordinator = provider.GetRequiredService<IRuntimeTransactionCoordinator>();

var workflow = new WorkflowInstance
{
    Key = new RuntimeInstanceKey("aot", "workflow"),
    WorkflowPin = Pin("workflow", "approval", "Workflow"),
    Variables = new Dictionary<string, RuntimeStateValue>(StringComparer.Ordinal)
    {
        ["message"] = new RuntimeStateValue { TypeId = "aot.string", JsonPayload = "\"native\"" }
    },
    StartedAt = DateTimeOffset.UnixEpoch
};
var task = new HumanTaskInstance
{
    Key = new RuntimeInstanceKey("aot", "task"),
    HumanTaskPin = Pin("humantask", "review", "HumanTask"),
    WorkflowKey = workflow.Key,
    WorkflowStepId = "review",
    Input = new RuntimeStateValue { TypeId = "aot.string", JsonPayload = "\"input\"" },
    CreatedAt = DateTimeOffset.UnixEpoch
};

await coordinator.ExecuteAsync(async ct =>
{
    await workflows.AddAsync(workflow, ct);
    await tasks.AddAsync(task, ct);
});

var restored = await workflows.GetAsync(workflow.Key);
if (restored?.Variables["message"].JsonPayload != "\"native\"")
    return 3;

var restoredTask = await tasks.GetAsync(task.Key);
if (restoredTask is null)
    return 4;

var suspended = restored.Snapshot();
suspended.Status = WorkflowInstanceStatus.Suspended;
suspended.WaitingHumanTaskKey = task.Key;
var receipt = new WorkflowSuspensionReceipt
{
    Scope = new RuntimeTenantScope("aot"),
    SuspensionOperationId = "aot-suspend",
    Integrity = Hash("aot-suspend", "Integrity"),
    WorkflowKey = workflow.Key,
    HumanTaskKey = task.Key,
    WorkflowFromRevision = restored.Revision,
    WorkflowToRevision = restored.Revision + 1,
    WorkflowPin = restored.WorkflowPin,
    HumanTaskPin = task.HumanTaskPin
};
await coordinator.ExecuteAsync(async ct =>
{
    (await receipts.AddAsync(receipt, ct)).Status.ShouldBeAccepted();
    await workflows.UpdateAsync(suspended, restored.Revision, ct);
});

Console.WriteLine("PHASE9B_POSTGRES_SUSPENSION_OK");

var duplicateResult = await receipts.AddAsync(receipt);
if (duplicateResult.Status != WorkflowSuspensionReceiptWriteStatus.Duplicate)
    return 5;

Console.WriteLine("PHASE9B_POSTGRES_RECEIPT_DEDUP_OK");

var envelope = new AuditEnvelope
{
    AuditId = "aot-audit",
    OccurredAt = DateTimeOffset.UnixEpoch,
    CorrelationId = "aot",
    Actor = new AuditActor { Kind = "system", Id = "aot" },
    Action = new AuditAction { Kind = "runtime", Name = "aot" },
    Target = new AuditTarget { Kind = "workflow", Id = workflow.InstanceId },
    Outcome = new AuditOutcome { Status = "succeeded" },
    Integrity = Hash("aot-audit", "AuditIntegrity")
};
var accepted = await provider.GetRequiredService<IAuditSink>().WriteAsync(envelope);
if (accepted.Status != AuditSinkWriteStatus.Accepted)
    return 6;

var freshProvider = new ServiceCollection()
    .AddCrestCreatesPostgreSqlRuntimePersistence(options)
    .BuildServiceProvider();
var freshWorkflows = freshProvider.GetRequiredService<IWorkflowInstanceStore>();
var freshTasks = freshProvider.GetRequiredService<IHumanTaskInstanceStore>();
var recoveredWorkflow = await freshWorkflows.GetAsync(workflow.Key);
var recoveredTask = await freshTasks.GetAsync(task.Key);
if (recoveredWorkflow?.Status != WorkflowInstanceStatus.Suspended)
    return 7;
if (recoveredWorkflow.WaitingHumanTaskKey != task.Key)
    return 8;
if (recoveredTask is null)
    return 9;

Console.WriteLine("PHASE9B_POSTGRES_RECOVERY_OK");
return 0;

static RuntimeDescriptorPin Pin(string @namespace, string id, string kind) => new()
{
    Ref = new DescriptorRef(@namespace, id, 1),
    ContractHash = Hash(id + "-contract", "Contract", kind),
    DefinitionHash = Hash(id + "-definition", "Definition", kind)
};

static CanonicalHash Hash(string value, string purpose, string? descriptorKind = null) => new()
{
    Value = value,
    Algorithm = "SHA-256",
    AlgorithmVersion = "sha256-canonical-json-v1",
    ArtifactKind = descriptorKind is null ? "Runtime" : "Descriptor",
    DescriptorKind = descriptorKind,
    Scope = "InternalFull",
    Purpose = purpose,
    ContractVersion = "canonical-hash-v1",
    CanonicalShapeVersion = "phase9b-aot-v1"
};

internal static class ReceiptResultExtensions
{
    public static void ShouldBeAccepted(this WorkflowSuspensionReceiptWriteStatus status)
    {
        if (status != WorkflowSuspensionReceiptWriteStatus.Accepted)
            throw new InvalidOperationException($"Expected accepted receipt, got '{status}'.");
    }
}
