using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Capability;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Registry;
using CrestCreates.Runtime.Delivery;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.InMemory;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public sealed class WorkflowAllPriorHumanTasksApprovedTests
{
    [Fact]
    public async Task ThreeSequentialGates_PersistTypedProofAndMachineEvidence()
    {
        await using var host = await StartAsync();
        var started = await host.Engine.ExecuteAsync(new WorkflowExecutionRequest
        {
            WorkflowId = WorkflowId, TenantId = "asset-tenant"
        });

        await CompletePendingAsync(host, started, "manager-approval", "Approve");
        var manager = (await host.Workflows.GetAsync(started.Key))!.StepResults.Last(x => x.StepId == "manager-approval");
        manager.HumanTaskOutcome.Should().Be(CompletionCondition.Approve);

        var evidence = host.State.Capture("machine-test-passed");
        await CompletePendingAsync(host, started, "machine-test-evidence", "Approve", evidence);
        var machine = (await host.Workflows.GetAsync(started.Key))!.StepResults.Last(x => x.StepId == "machine-test-evidence");
        machine.HumanTaskOutcome.Should().Be(CompletionCondition.Approve);
        machine.Output!.JsonPayload.Should().Be(evidence.JsonPayload);

        await CompletePendingAsync(host, started, "quality-approval", "Approve");
        await WaitAsync(async () => (await host.Workflows.GetAsync(started.Key))!.Status == WorkflowInstanceStatus.Completed);
        var completed = (await host.Workflows.GetAsync(started.Key))!;
        completed.StepResults.Where(x => x.Status == StepExecutionStatus.Completed).Select(x => x.HumanTaskOutcome)
            .Should().Equal(CompletionCondition.Approve, CompletionCondition.Approve, CompletionCondition.Approve);
        (await host.Tasks.GetPendingByWorkflowAsync(started.Key)).Should().BeEmpty();
    }

    [Fact]
    public async Task RejectedManager_SkipsMachineAndQualityWithoutMissingProofFault()
    {
        await using var host = await StartAsync();
        var started = await host.Engine.ExecuteAsync(new WorkflowExecutionRequest
        {
            WorkflowId = WorkflowId, TenantId = "asset-tenant"
        });

        await CompletePendingAsync(host, started, "manager-approval", "Reject");
        await WaitAsync(async () => (await host.Workflows.GetAsync(started.Key))!.Status == WorkflowInstanceStatus.Completed);
        var completed = (await host.Workflows.GetAsync(started.Key))!;
        completed.StepResults.Last(x => x.StepId == "manager-approval").HumanTaskOutcome.Should().Be(CompletionCondition.Reject);
        completed.StepResults.Where(x => x.StepId is "machine-test-evidence" or "quality-approval")
            .Should().OnlyContain(x => x.Status == StepExecutionStatus.Skipped && x.HumanTaskOutcome == null);
        (await host.Tasks.GetPendingByWorkflowAsync(started.Key)).Should().BeEmpty();
    }

    [Fact]
    public async Task MissingStepProof_FailsClosedBeforeCreatingQualityTask()
    {
        await using var host = await StartAsync();
        var started = await host.Engine.ExecuteAsync(new WorkflowExecutionRequest
        {
            WorkflowId = WorkflowId, TenantId = "asset-tenant"
        });
        await CompletePendingAsync(host, started, "manager-approval", "Approve");

        var damaged = (await host.Workflows.GetAsync(started.Key))!;
        damaged.StepResults.RemoveAll(x => x.StepId == "manager-approval");
        damaged.Status = WorkflowInstanceStatus.Running;
        damaged.StepIndex = 1;
        damaged.WaitingHumanTaskKey = null;
        await host.Workflows.UpdateAsync(damaged, damaged.Revision);

        var result = await host.Runner.RunAsync(
            (await host.Workflows.GetAsync(started.Key))!, "missing-proof-run", null, CancellationToken.None);

        result.Status.Should().Be(WorkflowInstanceStatus.Failed);
        result.ErrorMessage.Should().Contain("completed result").And.Contain("manager-approval");
        result.StepResults.Should().Contain(x => x.StepId == "machine-test-evidence" && x.Status == StepExecutionStatus.Failed);
        (await host.Tasks.GetPendingByWorkflowAsync(started.Key)).Should().NotContain(x => x.WorkflowStepId == "quality-approval");
    }

    private const string WorkflowId = "wf_asset_three_gate_prerequisite";

    private static async Task CompletePendingAsync(
        TestHost host, WorkflowInstance started, string stepId, string outcome, RuntimeStateValue? result = null)
    {
        await WaitAsync(async () => (await host.Tasks.GetPendingByWorkflowAsync(started.Key)).Any(x => x.WorkflowStepId == stepId));
        var task = (await host.Tasks.GetPendingByWorkflowAsync(started.Key)).Single(x => x.WorkflowStepId == stepId);
        await host.HumanTasks.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskKey = task.Key, Outcome = outcome, Result = result,
            ActorId = "asset-reviewer", ActorRoles = ["asset-reviewer"]
        });
        await WaitAsync(async () => (await host.Workflows.GetAsync(started.Key))!.StepResults
            .Any(x => x.StepId == stepId && x.Status == StepExecutionStatus.Completed && x.HumanTaskOutcome is not null));
    }

    private static async Task<TestHost> StartAsync()
    {
        var tasks = new[] { "ht_manager", "ht_machine_test", "ht_quality" }.Select(HumanTask).ToArray();
        var workflow = new WorkflowDescriptor
        {
            Id = WorkflowId, Name = "Asset three-gate approval", Version = 1, State = DescriptorState.Active,
            Steps =
            [
                Step("manager-approval", tasks[0]),
                Step("machine-test-evidence", tasks[1], WorkflowConditionTokens.PreviousHumanTaskApproved),
                Step("quality-approval", tasks[2], WorkflowConditionTokens.AllPriorHumanTasksApproved)
            ]
        };
        var workflowRegistry = new WorkflowRegistry(new RegistryValidationEngine<WorkflowDescriptor>([]));
        workflowRegistry.Build([new InlineProvider<WorkflowDescriptor>(workflow)]);
        var taskRegistry = new HumanTaskRegistry(new RegistryValidationEngine<HumanTaskDescriptor>([]));
        taskRegistry.Build([new InlineProvider<HumanTaskDescriptor>(tasks)]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddCrestLocalEventDispatch();
        services.AddAccountability().AddAuditSink<InMemoryAuditSink>();
        services.AddCapabilityRuntime();
        services.AddRuntimePersistence();
        services.AddCrestCreatesInMemoryRuntimePersistence();
        services.AddRuntimeDelivery(options => options.PollingInterval = TimeSpan.FromMilliseconds(10));
        services.AddHumanTaskRuntime();
        services.AddWorkflowEngine();
        services.AddSingleton<IWorkflowRegistry>(workflowRegistry);
        services.AddSingleton<IHumanTaskRegistry>(taskRegistry);
        var provider = services.BuildServiceProvider(validateScopes: true);
        var hosted = provider.GetServices<IHostedService>().ToArray();
        foreach (var service in hosted)
            await service.StartAsync(CancellationToken.None);
        var scope = provider.CreateScope();
        return new TestHost(provider, scope, hosted);

        static WorkflowStep Step(string id, HumanTaskDescriptor task, string? condition = null) => new()
        {
            Id = id, Name = id, Condition = condition,
            Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(task.Id, task.Version) }
        };
    }

    private static HumanTaskDescriptor HumanTask(string id) => new()
    {
        Id = id, Name = id, Version = 1, State = DescriptorState.Active,
        Outcomes = [new CompletionOutcome { Condition = CompletionCondition.Approve }, new CompletionOutcome { Condition = CompletionCondition.Reject }]
    };

    private static async Task WaitAsync(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            if (await predicate()) return;
            await Task.Delay(20, timeout.Token);
        }
        throw new TimeoutException("Workflow continuation did not reach the expected state.");
    }

    private sealed class TestHost(
        ServiceProvider provider, IServiceScope scope, IHostedService[] hosted) : IAsyncDisposable
    {
        public IWorkflowEngine Engine { get; } = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        public IWorkflowExecutionRunner Runner { get; } = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionRunner>();
        public IHumanTaskRuntime HumanTasks { get; } = scope.ServiceProvider.GetRequiredService<IHumanTaskRuntime>();
        public IWorkflowInstanceStore Workflows { get; } = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
        public IHumanTaskInstanceStore Tasks { get; } = scope.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();
        public IRuntimeStateContractRegistry State { get; } = scope.ServiceProvider.GetRequiredService<IRuntimeStateContractRegistry>();

        public async ValueTask DisposeAsync()
        {
            for (var index = hosted.Length - 1; index >= 0; index--)
                await hosted[index].StopAsync(CancellationToken.None);
            scope.Dispose();
            await provider.DisposeAsync();
        }
    }

    private sealed class InlineProvider<T>(params T[] descriptors) : IDescriptorProvider<T> where T : IDescriptor
    {
        public IReadOnlyList<T> GetDescriptors() => descriptors;
    }
}