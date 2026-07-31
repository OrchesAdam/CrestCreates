using CrestCreates.HumanTask.Abstractions;
using System;
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
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Xunit;

namespace CrestCreates.Runtime.Persistence.InMemory.Tests;

public sealed class InMemoryRuntimeProviderTests
{
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

    private static WorkflowInstance NewWorkflow(string? tenantId, string id) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowPin = Pin("workflow", "workflow", "Workflow")
    };

    private static HumanTaskInstance NewTask(string? tenantId, string id, RuntimeInstanceKey workflowKey, string? stepId = "step-1") => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowKey = workflowKey,
        WorkflowStepId = stepId,
        HumanTaskPin = Pin("humantask", "task", "HumanTask")
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
