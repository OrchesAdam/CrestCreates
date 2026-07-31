using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
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

await workflows.AddAsync(workflow);
await tasks.AddAsync(task);
var restored = await workflows.GetAsync(workflow.Key);
if (restored?.Variables["message"].JsonPayload != "\"native\"")
    return 3;

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
    return 4;

Console.WriteLine("PHASE9B_POSTGRES_NATIVEAOT_OK");
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
