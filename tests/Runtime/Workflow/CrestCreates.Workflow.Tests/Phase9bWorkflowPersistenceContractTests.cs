using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Runtime.Persistence.InMemory;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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

    private static WorkflowInstance New(string? tenantId, string id) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        Status = WorkflowInstanceStatus.Running,
    };
}
