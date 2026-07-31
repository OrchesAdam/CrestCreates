using CrestCreates.HumanTask.Abstractions;
using System;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.Providers;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.InMemory;
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

    private static WorkflowInstance NewWorkflow(string? tenantId, string id) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowPin = Pin("workflow", "workflow", "Workflow")
    };

    private static HumanTaskInstance NewTask(string? tenantId, string id, RuntimeInstanceKey workflowKey) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowKey = workflowKey,
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
