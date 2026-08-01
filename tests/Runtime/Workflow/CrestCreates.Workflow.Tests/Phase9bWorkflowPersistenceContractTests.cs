using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.InMemory;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public sealed class Phase9bWorkflowPersistenceContractTests
{
    [Fact]
    public async Task AddAndGet_ShouldAssignRevisionAndReturnDetachedSnapshot()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var store = provider.GetRequiredService<IWorkflowInstanceStore>();
        var instance = New("tenant-a", "wf-1");
        await store.AddAsync(instance);

        instance.Revision.Should().Be(0);
        var loaded = await store.GetAsync(instance.Key);
        loaded.Should().NotBeNull();
        loaded!.Revision.Should().Be(1);
        loaded.Status = WorkflowInstanceStatus.Completed;
        (await store.GetAsync(instance.Key))!.Status.Should().Be(WorkflowInstanceStatus.Running);
    }

    [Fact]
    public async Task ConcurrentUpdate_FromSameRevision_ShouldAllowOneWinner()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var store = provider.GetRequiredService<IWorkflowInstanceStore>();
        var instance = New("tenant-a", "wf-1");
        await store.AddAsync(instance);
        var first = (await store.GetAsync(instance.Key))!;
        var second = first.Snapshot();
        first.Status = WorkflowInstanceStatus.Completed;
        second.Status = WorkflowInstanceStatus.Failed;

        await store.UpdateAsync(first, 1);
        var act = () => store.UpdateAsync(second, 1);
        await Assert.ThrowsAsync<RuntimeConcurrencyException>(act);
        (await store.GetAsync(instance.Key))!.Status.Should().Be(WorkflowInstanceStatus.Completed);
    }

    [Fact]
    public async Task SameInstanceIdAcrossTenants_ShouldRemainDistinct()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var store = provider.GetRequiredService<IWorkflowInstanceStore>();
        var host = New(null, "same");
        var tenant = New("tenant-a", "same");
        await store.AddAsync(host);
        await store.AddAsync(tenant);

        (await store.GetAsync(host.Key)).Should().NotBeNull();
        (await store.GetAsync(tenant.Key)).Should().NotBeNull();
        (await store.GetAsync(host.Key)).Should().NotBeNull();
        (await store.GetAsync(tenant.Key)).Should().NotBeNull();
    }

    [Fact]
    public async Task SuspensionCommit_ShouldAtomicallyPersistWorkflowHumanTaskAndReceipt()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var receipts = provider.GetRequiredService<IWorkflowSuspensionReceiptStore>();
        var coordinator = provider.GetRequiredService<CrestCreates.Runtime.Persistence.Abstractions.Transactions.IRuntimeTransactionCoordinator>();
        var committer = new WorkflowSuspensionCommitter(coordinator, workflows, tasks, receipts, new TestCanonicalHashComputer());
        var workflow = New("tenant-a", "wf-atomic");
        await workflows.AddAsync(workflow);
        var before = (await workflows.GetAsync(workflow.Key))!;
        var suspended = before.Snapshot();
        var task = NewTask("tenant-a", "task-atomic", workflow.Key);
        suspended.Status = WorkflowInstanceStatus.Suspended;
        suspended.WaitingHumanTaskKey = task.Key;

        await committer.CommitAsync(before, suspended, task, "operation-atomic", CancellationToken.None);
        await committer.CommitAsync(before, suspended, task, "operation-atomic", CancellationToken.None);

        var persistedWorkflow = await workflows.GetAsync(workflow.Key);
        persistedWorkflow!.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        persistedWorkflow.WaitingHumanTaskKey.Should().Be(task.Key);
        persistedWorkflow.Revision.Should().Be(2);
        (await tasks.GetAsync(task.Key)).Should().NotBeNull();
        (await receipts.GetAsync(new RuntimeTenantScope("tenant-a"), "operation-atomic")).Should().NotBeNull();
    }

    [Fact]
    public async Task FailedSuspensionCommit_ShouldExposeNeitherWorkflowTransitionNorReceipt()
    {
        using var provider = new ServiceCollection().AddCrestCreatesInMemoryRuntimePersistence().BuildServiceProvider();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var receipts = provider.GetRequiredService<IWorkflowSuspensionReceiptStore>();
        var coordinator = provider.GetRequiredService<CrestCreates.Runtime.Persistence.Abstractions.Transactions.IRuntimeTransactionCoordinator>();
        var committer = new WorkflowSuspensionCommitter(coordinator, workflows, tasks, receipts, new TestCanonicalHashComputer());
        var workflow = New("tenant-a", "wf-rollback");
        await workflows.AddAsync(workflow);
        var before = (await workflows.GetAsync(workflow.Key))!;
        var suspended = before.Snapshot();
        var task = NewTask("tenant-a", "task-rollback", workflow.Key);
        suspended.Status = WorkflowInstanceStatus.Suspended;
        suspended.WaitingHumanTaskKey = task.Key;
        await tasks.AddAsync(task);

        var act = () => committer.CommitAsync(before, suspended, task, "operation-rollback", CancellationToken.None);

        await act.Should().ThrowAsync<RuntimeDuplicateEntityException>();
        (await workflows.GetAsync(workflow.Key))!.Status.Should().Be(WorkflowInstanceStatus.Running);
        (await receipts.GetAsync(new RuntimeTenantScope("tenant-a"), "operation-rollback")).Should().BeNull();
    }

    private static WorkflowInstance New(string? tenantId, string id) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowPin = Pin(),
        Status = WorkflowInstanceStatus.Running,
    };

    private static HumanTaskInstance NewTask(string? tenantId, string id, RuntimeInstanceKey workflowKey) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowKey = workflowKey,
        WorkflowStepId = "review",
        HumanTaskPin = new RuntimeDescriptorPin
        {
            Ref = new DescriptorRef("humantask", "review", 1),
            ContractHash = Hash("human-contract", "Contract", "HumanTask"),
            DefinitionHash = Hash("human-definition", "Definition", "HumanTask")
        }
    };

    private static RuntimeDescriptorPin Pin() => new()
    {
        Ref = new DescriptorRef("workflow", "approval", 1),
        ContractHash = Hash("contract", "Contract", "Workflow"),
        DefinitionHash = Hash("definition", "Definition", "Workflow")
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
        CanonicalShapeVersion = "workflow-v1"
    };

    private sealed class TestCanonicalHashComputer : ICanonicalHashComputer
    {
        public CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope)
            => throw new NotSupportedException();

        public CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope)
            => throw new NotSupportedException();

        public CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection)
        {
            var bufferWriter = new ArrayBufferWriter<byte>(4096);
            using var jsonWriter = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions
            {
                Indented = false,
                SkipValidation = true
            });
            projection.WriteCanonicalJson(jsonWriter);
            jsonWriter.Flush();

            var hashBytes = SHA256.HashData(bufferWriter.WrittenSpan);
            return new CanonicalHash
            {
                Value = Convert.ToHexString(hashBytes).ToLowerInvariant(),
                Algorithm = "SHA-256",
                AlgorithmVersion = projection.Metadata.AlgorithmVersion,
                ArtifactKind = projection.Metadata.ArtifactKind,
                Scope = projection.Metadata.Scope,
                Purpose = projection.Metadata.Purpose,
                ContractVersion = projection.Metadata.ContractVersion,
                CanonicalShapeVersion = projection.Metadata.CanonicalShapeVersion
            };
        }
    }
}
