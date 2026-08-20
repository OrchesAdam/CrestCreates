using CrestCreates.HumanTask.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.Providers;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.InMemory;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Runtime.Persistence.Testing.Cases;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Xunit;

namespace CrestCreates.Runtime.Persistence.InMemory.Tests;

public sealed class InMemoryRuntimeProviderTests
{
    [Fact]
    public async Task SharedRuntimeContractKit_ShouldPass()
    {
        using var driver = new InMemoryRuntimePersistenceContractDriver($"shared-{Guid.NewGuid():N}");
        await RuntimePersistenceContractCases.DescriptorSnapshot_IdentityAndOrderingAsync(driver);
        await RuntimePersistenceContractCases.HumanTask_QueryOrderAsync(driver);
        await RuntimePersistenceContractCases.Workflow_RevisionAndTransactionAsync(driver);
    }
    [Fact]
    public void Provider_ShouldDeclareFullSemanticWithoutDurability()
    {
        var capabilities = new InMemoryRuntimeProviderCapabilities();
        capabilities.Tier.Should().Be(RuntimePersistenceProviderTier.FullSemantic);
        capabilities.SupportsAtomicMultiStoreTransactions.Should().BeTrue();
        capabilities.SupportsProcessDurability.Should().BeFalse();
    }

    [Fact]
    public async Task NestedTransaction_ShouldJoinOuterAndRollbackBothStores()
    {
        var services = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var tx = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var workflow = NewWorkflow("tenant-a", "wf-1");
        var task = NewTask("tenant-a", "task-1", workflow.Key);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await tx.ExecuteAsync(async _ =>
            {
                await workflows.AddAsync(workflow);
                await tx.ExecuteAsync(async __ =>
                {
                    await tasks.AddAsync(task);
                    throw new InvalidOperationException("rollback");
                });
            }));

        (await workflows.GetAsync(workflow.Key)).Should().BeNull();
        (await tasks.GetAsync(task.Key)).Should().BeNull();
    }

    [Fact]
    public async Task SameTenantKey_ShouldBeCasProtected()
    {
        var services = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IWorkflowInstanceStore>();
        var item = NewWorkflow("tenant-a", "wf-1");
        await store.AddAsync(item);
        var stale = (await store.GetAsync(item.Key))!;
        await store.UpdateAsync(stale, 1);
        var act = () => store.UpdateAsync(stale, 1);
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task SnapshotAndReceiptStores_ShouldBeImmutableAndIdempotent()
    {
        var services = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var snapshots = provider.GetRequiredService<IDescriptorSnapshotStore>();
        var snapshot = new DescriptorSnapshot { SnapshotId = "snap-1", PackageId = "pkg", PackageVersion = "1", Descriptors = new[] { new SnapshotEntry { Ref = new DescriptorRef("workflow", "wf", 1), ContractHash = "c", DefinitionHash = "d" } } };
        (await snapshots.WriteAsync(snapshot)).Status.Should().Be(DescriptorSnapshotWriteStatus.Accepted);
        (await snapshots.WriteAsync(snapshot)).Status.Should().Be(DescriptorSnapshotWriteStatus.Duplicate);
        snapshot.Descriptors[0].DefinitionHash.Should().Be("d");
    }

    [Fact]
    public async Task DescriptorSnapshotIdentity_ShouldIncludeEveryPersistedField()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var store = provider.GetRequiredService<IDescriptorSnapshotStore>();
        var baseline = CompleteSnapshot();
        (await store.WriteAsync(baseline)).Status.Should().Be(DescriptorSnapshotWriteStatus.Accepted);

        var descriptorName = CopyWith(baseline, descriptors: [CopyEntry(baseline.Descriptors[0], descriptorName: "Renamed")]);
        var kind = CopyWith(baseline, descriptors: [CopyEntry(baseline.Descriptors[0], kind: DescriptorKind.HumanTask)]);
        var state = CopyWith(baseline, descriptors: [CopyEntry(baseline.Descriptors[0], state: DescriptorState.Deprecated)]);
        var superseded = CopyWith(baseline, descriptors: [CopyEntry(baseline.Descriptors[0], supersededById: "next")]);
        var created = CopyWith(baseline, createdAt: baseline.CreatedAt.AddSeconds(1));
        var relationship = CopyWith(baseline, relationships: [baseline.Relationships[0] with { Role = "changed" }]);

        foreach (var changed in new[] { descriptorName, kind, state, superseded, created, relationship })
        {
            (await store.WriteAsync(changed)).Status.Should().Be(DescriptorSnapshotWriteStatus.Conflict);
        }
    }

    [Fact]
    public async Task DescriptorSnapshotIdentity_ShouldNormalizeCollectionOrder()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var store = provider.GetRequiredService<IDescriptorSnapshotStore>();
        var baseline = CompleteSnapshot();
        (await store.WriteAsync(baseline)).Status.Should().Be(DescriptorSnapshotWriteStatus.Accepted);

        var reordered = CopyWith(baseline,
            descriptors: baseline.Descriptors.Reverse().ToArray(),
            relationships: baseline.Relationships.Reverse().ToArray());
        (await store.WriteAsync(reordered)).Status.Should().Be(DescriptorSnapshotWriteStatus.Duplicate);
    }

    [Fact]
    public async Task HumanTaskQueries_ShouldOrderByCreatedAtThenInstanceId()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var workflow = NewWorkflow("tenant-a", "workflow-order");
        await workflows.AddAsync(workflow);
        var later = NewTask("tenant-a", "task-z", workflow.Key, stepId: "review-z", createdAt: DateTimeOffset.UnixEpoch.AddMinutes(2));
        var earlier = NewTask("tenant-a", "task-a", workflow.Key, stepId: "review-a", createdAt: DateTimeOffset.UnixEpoch.AddMinutes(1));
        await tasks.AddAsync(later);
        await tasks.AddAsync(earlier);

        var result = await tasks.GetPendingByWorkflowAsync(workflow.Key);
        result.Select(x => x.Key.InstanceId).Should().Equal("task-a", "task-z");
    }

    private static DescriptorSnapshot CompleteSnapshot() => new()
    {
        SnapshotId = "snapshot-contract",
        PackageId = "package",
        PackageVersion = "1.0.0",
        CreatedAt = DateTimeOffset.UnixEpoch,
        Descriptors =
        [
            new SnapshotEntry
            {
                Ref = new DescriptorRef("workflow", "approval", 1),
                DescriptorName = "Approval",
                Kind = DescriptorKind.Workflow,
                State = DescriptorState.Active,
                ContractHash = "contract",
                DefinitionHash = "definition",
                SupersededById = null
            }
        ],
        Relationships =
        [
            new CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackageRelationshipEntry
            {
                From = new DescriptorRef("workflow", "approval", 1),
                To = new DescriptorRef("humantask", "review", 1),
                Kind = RelationshipKind.Uses,
                Role = "review",
                SourcePath = "steps.review",
                Strength = RelationshipStrength.Strong,
                IsRuntimeBinding = true
            }
        ]
    };

    private static DescriptorSnapshot CopyWith(
        DescriptorSnapshot source,
        IReadOnlyList<SnapshotEntry>? descriptors = null,
        IReadOnlyList<CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackageRelationshipEntry>? relationships = null,
        DateTimeOffset? createdAt = null) => new()
    {
        SnapshotId = source.SnapshotId,
        PackageId = source.PackageId,
        PackageVersion = source.PackageVersion,
        CreatedAt = createdAt ?? source.CreatedAt,
        Descriptors = descriptors ?? source.Descriptors,
        Relationships = relationships ?? source.Relationships
    };

    private static SnapshotEntry CopyEntry(
        SnapshotEntry source,
        string? descriptorName = null,
        DescriptorKind? kind = null,
        DescriptorState? state = null,
        string? supersededById = null) => new()
    {
        Ref = source.Ref,
        DescriptorName = descriptorName ?? source.DescriptorName,
        Kind = kind ?? source.Kind,
        State = state ?? source.State,
        ContractHash = source.ContractHash,
        DefinitionHash = source.DefinitionHash,
        SupersededById = supersededById ?? source.SupersededById
    };

    [Fact]
    public async Task ConcurrentUseOfAmbientSession_ShouldFailClosed()
    {
        var services = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var tx = (InMemoryRuntimeTransactionCoordinator)provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var workflow = NewWorkflow("tenant-a", "wf-1");
        await provider.GetRequiredService<IWorkflowInstanceStore>().AddAsync(workflow);
        var task1 = NewTask("tenant-a", "task-1", workflow.Key);

        await tx.ExecuteAsync(async ct =>
        {
            using var hold = tx.EnterStoreOperation();
            var act = async () => await tasks.AddAsync(task1, ct);
            await act.Should().ThrowAsync<RuntimePersistenceContractException>()
                .Where(ex => ex.Code == RuntimePersistenceContractErrorCode.ConcurrentAmbientUse);
        });
    }

    [Fact]
    public async Task SequentialNestedCalls_ShouldSucceed()
    {
        var services = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var tx = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var workflow = NewWorkflow("tenant-a", "wf-1");
        var task = NewTask("tenant-a", "task-1", workflow.Key);

        await tx.ExecuteAsync(async ct =>
        {
            await workflows.AddAsync(workflow, ct);
            await tasks.AddAsync(task, ct);
        });

        (await workflows.GetAsync(workflow.Key)).Should().NotBeNull();
        (await tasks.GetAsync(task.Key)).Should().NotBeNull();
    }

    [Fact]
    public async Task ActiveStepConflict_ShouldFailWhenTwoActiveTasksShareSameWorkflowStep()
    {
        var services = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var tx = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var workflow = NewWorkflow("tenant-a", "wf-1");
        var task1 = NewTask("tenant-a", "task-1", workflow.Key, "step-1");
        var task2 = NewTask("tenant-a", "task-2", workflow.Key, "step-1");

        await tx.ExecuteAsync(async ct =>
        {
            await workflows.AddAsync(workflow, ct);
            await tasks.AddAsync(task1, ct);
        });

        var act = async () => await tx.ExecuteAsync(async ct => await tasks.AddAsync(task2, ct));
        await act.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(ex => ex.Code == RuntimePersistenceContractErrorCode.ActiveStepCorrelationConflict);
    }

    [Fact]
    public async Task CompletedTask_ShouldAllowNewActiveTaskForSameWorkflowStep()
    {
        var services = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var tx = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var workflow = NewWorkflow("tenant-a", "wf-1");
        var task1 = NewTask("tenant-a", "task-1", workflow.Key, "step-1");
        task1.Status = HumanTaskInstanceStatus.Completed;
        task1.CompletedAt = DateTimeOffset.UtcNow;

        await tx.ExecuteAsync(async ct =>
        {
            await workflows.AddAsync(workflow, ct);
            await tasks.AddAsync(task1, ct);
        });

        var task2 = NewTask("tenant-a", "task-2", workflow.Key, "step-1");
        var act = async () => await tx.ExecuteAsync(async ct => await tasks.AddAsync(task2, ct));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CancelledTask_ShouldAllowNewActiveTaskForSameWorkflowStep()
    {
        var services = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var tx = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var workflow = NewWorkflow("tenant-a", "wf-1");
        var task1 = NewTask("tenant-a", "task-1", workflow.Key, "step-1");
        task1.Status = HumanTaskInstanceStatus.Cancelled;
        task1.CancelledAt = DateTimeOffset.UtcNow;

        await tx.ExecuteAsync(async ct =>
        {
            await workflows.AddAsync(workflow, ct);
            await tasks.AddAsync(task1, ct);
        });

        var task2 = NewTask("tenant-a", "task-2", workflow.Key, "step-1");
        var act = async () => await tx.ExecuteAsync(async ct => await tasks.AddAsync(task2, ct));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Receipt_WithMissingWorkflowOrTask_ShouldFail()
    {
        var services = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence();
        using var provider = services.BuildServiceProvider();
        var tx = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var receipts = provider.GetRequiredService<IWorkflowSuspensionReceiptStore>();
        var workflow = NewWorkflow("tenant-a", "wf-1");
        var task = NewTask("tenant-a", "task-1", workflow.Key);

        var receipt = new WorkflowSuspensionReceipt
        {
            Scope = new RuntimeTenantScope("tenant-a"),
            SuspensionOperationId = "op-1",
            Integrity = Hash("integrity", "Integrity", "Suspension"),
            WorkflowKey = workflow.Key,
            HumanTaskKey = task.Key,
            WorkflowFromRevision = 1,
            WorkflowToRevision = 2,
            WorkflowPin = Pin("workflow", "workflow", "Workflow"),
            HumanTaskPin = Pin("humantask", "task", "HumanTask")
        };

        var act = async () => await tx.ExecuteAsync(async ct => await receipts.AddAsync(receipt, ct));
        await act.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(ex => ex.Code == RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Fact]
    public async Task DirectStoreWrite_WithNonReciprocalTask_ShouldFail()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var workflowA = NewWorkflow("tenant-a", "workflow-a");
        var workflowB = NewWorkflow("tenant-a", "workflow-b");
        await workflows.AddAsync(workflowA);
        await workflows.AddAsync(workflowB);
        var task = NewTask("tenant-a", "task-a", workflowB.Key);
        await tasks.AddAsync(task);
        var waiting = (await workflows.GetAsync(workflowA.Key))!;
        waiting.Status = WorkflowInstanceStatus.Suspended;
        waiting.WaitingHumanTaskKey = task.Key;

        var act = () => workflows.UpdateAsync(waiting, waiting.Revision);
        await act.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(ex => ex.Code == RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict);
    }

    [Fact]
    public async Task Receipt_WithTaskBelongingToAnotherWorkflow_ShouldFail()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var receipts = provider.GetRequiredService<IWorkflowSuspensionReceiptStore>();
        var workflowA = NewWorkflow("tenant-a", "workflow-a");
        var workflowB = NewWorkflow("tenant-a", "workflow-b");
        await workflows.AddAsync(workflowA);
        await workflows.AddAsync(workflowB);
        var task = NewTask("tenant-a", "task-a", workflowB.Key);
        await tasks.AddAsync(task);
        var receipt = new WorkflowSuspensionReceipt
        {
            Scope = new RuntimeTenantScope("tenant-a"),
            SuspensionOperationId = "wrong-workflow",
            Integrity = Hash("integrity", "Integrity", "Suspension"),
            WorkflowKey = workflowA.Key,
            HumanTaskKey = task.Key,
            WorkflowFromRevision = 1,
            WorkflowToRevision = 2,
            WorkflowPin = workflowA.WorkflowPin,
            HumanTaskPin = task.HumanTaskPin
        };

        var act = () => receipts.AddAsync(receipt);
        await act.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(ex => ex.Code == RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict);
    }

    [Fact]
    public async Task AssignedTask_WithCompletedAt_ShouldFail()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var workflow = NewWorkflow("tenant-a", "workflow-1");
        await provider.GetRequiredService<IWorkflowInstanceStore>().AddAsync(workflow);
        var task = NewTask("tenant-a", "task-1", workflow.Key);
        task.CompletedAt = DateTimeOffset.UtcNow;

        var act = () => provider.GetRequiredService<IHumanTaskInstanceStore>().AddAsync(task);
        await act.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(ex => ex.Code == RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Fact]
    public async Task CompletedTask_WithoutCompletedAt_ShouldFail()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var workflow = NewWorkflow("tenant-a", "workflow-1");
        await provider.GetRequiredService<IWorkflowInstanceStore>().AddAsync(workflow);
        var task = NewTask("tenant-a", "task-1", workflow.Key);
        task.Status = HumanTaskInstanceStatus.Completed;

        var act = () => provider.GetRequiredService<IHumanTaskInstanceStore>().AddAsync(task);
        await act.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(ex => ex.Code == RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Fact]
    public async Task CancelledTask_WithoutCancelledAt_ShouldFail()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var workflow = NewWorkflow("tenant-a", "workflow-1");
        await provider.GetRequiredService<IWorkflowInstanceStore>().AddAsync(workflow);
        var task = NewTask("tenant-a", "task-1", workflow.Key);
        task.Status = HumanTaskInstanceStatus.Cancelled;

        var act = () => provider.GetRequiredService<IHumanTaskInstanceStore>().AddAsync(task);
        await act.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(ex => ex.Code == RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    private static WorkflowInstance NewWorkflow(string? tenantId, string id) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowPin = Pin("workflow", "workflow", "Workflow")
    };

    private static HumanTaskInstance NewTask(string? tenantId, string id, RuntimeInstanceKey workflowKey, string? stepId = "step-1", DateTimeOffset? createdAt = null) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowKey = workflowKey,
        WorkflowStepId = stepId,
        HumanTaskPin = Pin("humantask", "task", "HumanTask"),
        CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch
    };

    private static RuntimeDescriptorPin Pin(string @namespace, string id, string kind) => new()
    {
        Ref = new DescriptorRef(@namespace, id, 1),
        ContractHash = Hash("contract", "Contract", kind),
        DefinitionHash = Hash("definition", "Definition", kind)
    };

    private static CanonicalHash Hash(string value, string purpose, string kind) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "Descriptor",
        DescriptorKind = kind,
        Scope = "InternalFull",
        Purpose = purpose,
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "runtime-test-v1"
    };
}
