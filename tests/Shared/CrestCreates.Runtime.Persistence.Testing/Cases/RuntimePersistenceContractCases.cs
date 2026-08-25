using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Testing.Assertions;
using CrestCreates.Runtime.Persistence.Testing.Contracts;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Runtime.Persistence.Testing.Cases;

/// <summary>
/// The first shared Runtime kit. Keep cases provider-neutral and expand this
/// ledger as the remaining Phase 9b contracts are migrated from provider tests.
/// </summary>
public static class RuntimePersistenceContractCases
{
    public static async Task DescriptorSnapshot_IdentityAndOrderingAsync(IRuntimePersistenceContractDriver driver)
    {
        var baseline = Snapshot(driver.ScopeId);
        Equal(DescriptorSnapshotWriteStatus.Accepted, (await driver.Snapshots.WriteAsync(baseline)).Status, "first snapshot write");
        Equal(DescriptorSnapshotWriteStatus.Duplicate, (await driver.Snapshots.WriteAsync(Copy(baseline))).Status, "identical retry");

        var changedSnapshots = new[]
        {
            Copy(baseline, descriptorName: "changed"),
            Copy(baseline, kind: DescriptorKind.HumanTask),
            Copy(baseline, state: DescriptorState.Deprecated),
            Copy(baseline, supersededById: "next"),
            Copy(baseline, createdAt: baseline.CreatedAt.AddSeconds(1)),
            Copy(baseline, relationshipRole: "changed")
        };
        foreach (var changed in changedSnapshots)
            Equal(DescriptorSnapshotWriteStatus.Conflict, (await driver.Snapshots.WriteAsync(changed)).Status, "persisted field mutation");

        var reordered = Copy(baseline,
            descriptors: baseline.Descriptors.Reverse().ToArray(),
            relationships: baseline.Relationships.Reverse().ToArray());
        Equal(DescriptorSnapshotWriteStatus.Duplicate, (await driver.Snapshots.WriteAsync(reordered)).Status, "collection order normalization");
    }

    public static async Task HumanTask_QueryOrderAsync(IRuntimePersistenceContractDriver driver)
    {
        var workflow = Workflow(driver.ScopeId, "workflow-order");
        await driver.Workflows.AddAsync(workflow);
        await driver.HumanTasks.AddAsync(HumanTask(driver.ScopeId, "task-z", workflow.Key, "step-z", DateTimeOffset.UnixEpoch.AddMinutes(2)));
        await driver.HumanTasks.AddAsync(HumanTask(driver.ScopeId, "task-a", workflow.Key, "step-a", DateTimeOffset.UnixEpoch.AddMinutes(1)));

        var result = await driver.HumanTasks.GetPendingByWorkflowAsync(workflow.Key);
        Equal(2, result.Count, "pending workflow query count");
        Equal("task-a", result[0].Key.InstanceId, "pending query order first");
        Equal("task-z", result[1].Key.InstanceId, "pending query order second");
    }

    public static async Task Workflow_RevisionAndTransactionAsync(IRuntimePersistenceContractDriver driver)
    {
        var workflow = Workflow(driver.ScopeId, "workflow-revision");
        await driver.Workflows.AddAsync(workflow);
        var loaded = await driver.Workflows.GetAsync(workflow.Key);
        True(loaded is not null, "workflow must be readable after insert");
        Equal(1L, loaded!.Revision, "insert revision");
        loaded.Status = WorkflowInstanceStatus.Completed;
        await driver.Workflows.UpdateAsync(loaded, 1);
        Equal(WorkflowInstanceStatus.Completed, (await driver.Workflows.GetAsync(workflow.Key))!.Status, "updated workflow status");

        var rolledBack = Workflow(driver.ScopeId, "workflow-rollback");
        await ThrowsAsync<InvalidOperationException>(async () => await driver.Transactions.ExecuteAsync(async _ =>
        {
            await driver.Workflows.AddAsync(rolledBack);
            throw new InvalidOperationException("contract rollback");
        }));
        True(await driver.Workflows.GetAsync(rolledBack.Key) is null, "rollback must hide inserted workflow");
    }

    private static DescriptorSnapshot Snapshot(string scope) => new()
    {
        SnapshotId = $"snapshot-{scope}",
        PackageId = "package",
        PackageVersion = "1.0.0",
        CreatedAt = DateTimeOffset.UnixEpoch,
        Descriptors =
        [new SnapshotEntry
        {
            Ref = new DescriptorRef("workflow", "approval", 1),
            DescriptorName = "Approval",
            Kind = DescriptorKind.Workflow,
            State = DescriptorState.Active,
            ContractHash = "contract",
            DefinitionHash = "definition"
        }],
        Relationships =
        [new DescriptorPackageRelationshipEntry
        {
            From = new DescriptorRef("workflow", "approval", 1),
            To = new DescriptorRef("humantask", "review", 1),
            Kind = RelationshipKind.Uses,
            Role = "review",
            SourcePath = "steps.review",
            Strength = RelationshipStrength.Strong,
            IsRuntimeBinding = true
        }]
    };

    private static DescriptorSnapshot Copy(
        DescriptorSnapshot source,
        string? descriptorName = null,
        DescriptorKind? kind = null,
        DescriptorState? state = null,
        string? supersededById = null,
        IReadOnlyList<SnapshotEntry>? descriptors = null,
        IReadOnlyList<DescriptorPackageRelationshipEntry>? relationships = null,
        DateTimeOffset? createdAt = null,
        string? relationshipRole = null) => new()
    {
        SnapshotId = source.SnapshotId,
        PackageId = source.PackageId,
        PackageVersion = source.PackageVersion,
        CreatedAt = createdAt ?? source.CreatedAt,
        Descriptors = descriptors ?? [new SnapshotEntry
        {
            Ref = source.Descriptors[0].Ref,
            DescriptorName = descriptorName ?? source.Descriptors[0].DescriptorName,
            Kind = kind ?? source.Descriptors[0].Kind,
            State = state ?? source.Descriptors[0].State,
            ContractHash = source.Descriptors[0].ContractHash,
            DefinitionHash = source.Descriptors[0].DefinitionHash,
            SupersededById = supersededById ?? source.Descriptors[0].SupersededById
        }],
        Relationships = relationships ?? [source.Relationships[0] with { Role = relationshipRole ?? source.Relationships[0].Role }]
    };

    private static WorkflowInstance Workflow(string scope, string id) => new()
    {
        Key = new RuntimeInstanceKey(scope, id),
        WorkflowPin = Pin("workflow", "approval", "Workflow"),
        StartedAt = DateTimeOffset.UnixEpoch
    };

    private static HumanTaskInstance HumanTask(string scope, string id, RuntimeInstanceKey workflow, string step, DateTimeOffset createdAt) => new()
    {
        Key = new RuntimeInstanceKey(scope, id),
        WorkflowKey = workflow,
        WorkflowStepId = step,
        RequiredCompletionConsumerIds = ["crest.workflow.humantask-continuation/v1"],
        CreatedAt = createdAt,
        HumanTaskPin = Pin("humantask", "review", "HumanTask")
    };

    private static RuntimeDescriptorPin Pin(string @namespace, string id, string kind) => new()
    {
        Ref = new DescriptorRef(@namespace, id, 1),
        ContractHash = new CanonicalHash { Value = $"{id}-contract", Algorithm = "SHA-256", AlgorithmVersion = "test", ArtifactKind = "Descriptor", DescriptorKind = kind, Scope = "InternalFull", Purpose = "Contract", ContractVersion = "test", CanonicalShapeVersion = "test" },
        DefinitionHash = new CanonicalHash { Value = $"{id}-definition", Algorithm = "SHA-256", AlgorithmVersion = "test", ArtifactKind = "Descriptor", DescriptorKind = kind, Scope = "InternalFull", Purpose = "Definition", ContractVersion = "test", CanonicalShapeVersion = "test" }
    };

    private static void True(bool condition, string message) => RuntimePersistenceContractAssertions.True(condition, message);
    private static void Equal<T>(T expected, T actual, string message) where T : notnull => RuntimePersistenceContractAssertions.Equal(expected, actual, message);

    private static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }
        throw new RuntimePersistenceContractAssertionException($"Expected {typeof(TException).Name}.");
    }
}
