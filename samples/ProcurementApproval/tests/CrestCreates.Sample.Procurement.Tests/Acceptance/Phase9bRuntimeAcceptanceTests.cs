using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.InMemory;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Sample.Procurement.Tests.Acceptance;

public sealed class Phase9bRuntimeAcceptanceTests
{
    [Fact]
    public async Task SuspensionCommit_RollsBackWorkflowAndHumanTaskTogether()
    {
        using var provider = new ServiceCollection()
            .AddCrestCreatesInMemoryRuntimePersistence()
            .BuildServiceProvider();
        var coordinator = provider.GetRequiredService<IRuntimeTransactionCoordinator>();
        var workflows = provider.GetRequiredService<IWorkflowInstanceStore>();
        var tasks = provider.GetRequiredService<IHumanTaskInstanceStore>();
        var workflow = new WorkflowInstance { Key = new RuntimeInstanceKey("tenant-a", "workflow-1") };
        var task = new HumanTaskInstance { Key = new RuntimeInstanceKey("tenant-a", "task-1"), WorkflowKey = workflow.Key };

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteAsync(async _ =>
        {
            await workflows.AddAsync(workflow);
            await tasks.AddAsync(task);
            throw new InvalidOperationException("synthetic crash before commit");
        }).AsTask());

        Assert.Null(await workflows.GetAsync(workflow.Key));
        Assert.Null(await tasks.GetAsync(task.Key));
    }

    [Fact]
    public async Task RuntimeLookup_UsesTenantScopedCompositeKeys()
    {
        using var provider = new ServiceCollection()
            .AddCrestCreatesInMemoryRuntimePersistence()
            .BuildServiceProvider();
        var store = provider.GetRequiredService<IWorkflowInstanceStore>();
        var host = new WorkflowInstance { Key = new RuntimeInstanceKey(null, "same-id") };
        var tenant = new WorkflowInstance { Key = new RuntimeInstanceKey("tenant-a", "same-id") };

        await store.AddAsync(host);
        await store.AddAsync(tenant);

        Assert.NotNull(await store.GetAsync(host.Key));
        Assert.NotNull(await store.GetAsync(tenant.Key));
    }
}
