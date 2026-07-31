using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
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
        var workflow = NewWorkflow("tenant-a", "workflow-1");
        var task = NewHumanTask("tenant-a", "task-1", workflow.Key);

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
        var host = NewWorkflow(null, "same-id");
        var tenant = NewWorkflow("tenant-a", "same-id");

        await store.AddAsync(host);
        await store.AddAsync(tenant);

        Assert.NotNull(await store.GetAsync(host.Key));
        Assert.NotNull(await store.GetAsync(tenant.Key));
    }

    private static WorkflowInstance NewWorkflow(string? tenantId, string id) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowPin = new RuntimeDescriptorPin
        {
            Ref = new DescriptorRef("workflow", "procurement-approval", 1),
            ContractHash = Hash("workflow-contract", "Contract", "Workflow"),
            DefinitionHash = Hash("workflow-definition", "Definition", "Workflow")
        }
    };

    private static HumanTaskInstance NewHumanTask(string? tenantId, string id, RuntimeInstanceKey workflowKey) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowKey = workflowKey,
        WorkflowStepId = "review",
        HumanTaskPin = new RuntimeDescriptorPin
        {
            Ref = new DescriptorRef("humantask", "procurement-review", 1),
            ContractHash = Hash("human-task-contract", "Contract", "HumanTask"),
            DefinitionHash = Hash("human-task-definition", "Definition", "HumanTask")
        }
    };

    private static CanonicalHash Hash(string value, string purpose, string descriptorKind) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "Descriptor",
        DescriptorKind = descriptorKind,
        Scope = "InternalFull",
        Purpose = purpose,
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "phase9b-sample-v1"
    };
}
