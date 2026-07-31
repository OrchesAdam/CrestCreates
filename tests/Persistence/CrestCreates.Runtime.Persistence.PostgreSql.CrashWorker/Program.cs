using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: <connection-string> <schema> <operation-id> <application-name>");
    return 2;
}

var options = new PostgreSqlRuntimePersistenceOptions { ConnectionString = args[0], Schema = args[1] };
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
    CreatedAt = DateTimeOffset.UnixEpoch
};
var suspended = before.Snapshot();
suspended.Status = WorkflowInstanceStatus.Suspended;
suspended.WaitingHumanTaskKey = task.Key;
var receipt = new WorkflowSuspensionReceipt
{
    Scope = new RuntimeTenantScope("crash"),
    SuspensionOperationId = args[2],
    Integrity = Hash(args[2], "Integrity"),
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

Console.WriteLine($"COMMITTED {args[2]}");
Console.Out.Flush();
await Task.Delay(TimeSpan.FromMinutes(2));
return 0;

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
