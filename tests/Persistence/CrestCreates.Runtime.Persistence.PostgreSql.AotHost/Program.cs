using System.Text.Json.Serialization;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Core.Abstractions.Serialization;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: <connection-string> <schema>");
    return 2;
}

var options = new PostgreSqlRuntimePersistenceOptions { ConnectionString = args[0], Schema = args[1] };
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

return 0;

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
