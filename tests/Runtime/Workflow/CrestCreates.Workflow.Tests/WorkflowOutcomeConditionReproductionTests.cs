using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Capability;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Registry;
using CrestCreates.Runtime.Delivery;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.InMemory;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.Workflow.Tests;

/// <summary>
/// B04 reproduction for the Phase 10c Asset scenario. This intentionally
/// remains red until WorkflowStep.Condition is consumed by the runner.
/// </summary>
public sealed class WorkflowOutcomeConditionReproductionTests
{
    [Fact]
    public async Task InitialReject_DoesNotCreateFinalApprovalTask()
    {
        var initialReview = HumanTask("ht_asset_maintenance_initial_review");
        var finalReview = HumanTask("ht_asset_maintenance_final_review");
        var workflow = new WorkflowDescriptor
        {
            Id = "wf_asset_maintenance_v2_reproduction",
            Name = "Asset maintenance v2 reproduction",
            Version = 1,
            State = DescriptorState.Active,
            Steps =
            [
                new WorkflowStep
                {
                    Id = "initial-review",
                    Name = "Initial maintenance review",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(initialReview.Id, 1)
                    }
                },
                new WorkflowStep
                {
                    Id = "final-review",
                    Name = "Final maintenance approval",
                    Condition = "previous-human-task-approved",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(finalReview.Id, 1)
                    }
                }
            ]
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountability().AddAuditSink<InMemoryAuditSink>();
        services.AddCapabilityRuntime();
        services.AddRuntimePersistence();
        services.AddCrestCreatesInMemoryRuntimePersistence();
        services.AddRuntimeDelivery(options => options.PollingInterval = TimeSpan.FromMilliseconds(10));
        services.AddHumanTaskRuntime();
        services.AddWorkflowEngine();

        var workflowRegistry = new WorkflowRegistry(new RegistryValidationEngine<WorkflowDescriptor>([]));
        workflowRegistry.Build([new InlineDescriptorProvider<WorkflowDescriptor>(workflow)]);
        services.AddSingleton<IWorkflowRegistry>(workflowRegistry);

        var humanTaskRegistry = new HumanTaskRegistry(new RegistryValidationEngine<HumanTaskDescriptor>([]));
        humanTaskRegistry.Build([new InlineDescriptorProvider<HumanTaskDescriptor>(initialReview, finalReview)]);
        services.AddSingleton<IHumanTaskRegistry>(humanTaskRegistry);

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        foreach (var hostedService in hostedServices)
            await hostedService.StartAsync(CancellationToken.None);

        try
        {
            using var scope = provider.CreateScope();
            var serviceProvider = scope.ServiceProvider;
            var engine = serviceProvider.GetRequiredService<IWorkflowEngine>();
            var humanTasks = serviceProvider.GetRequiredService<IHumanTaskRuntime>();
            var workflowStore = serviceProvider.GetRequiredService<IWorkflowInstanceStore>();
            var taskStore = serviceProvider.GetRequiredService<IHumanTaskInstanceStore>();

            var started = await engine.ExecuteAsync(new WorkflowExecutionRequest
            {
                WorkflowId = workflow.Id,
                TenantId = "asset-tenant",
                InputVariables = new Dictionary<string, CrestCreates.Runtime.Persistence.Abstractions.State.RuntimeStateValue>()
            });
            var firstTask = (await taskStore.GetPendingByWorkflowAsync(started.Key)).Single();
            firstTask.WorkflowStepId.Should().Be("initial-review");

            await humanTasks.CompleteAsync(new HumanTaskCompletionRequest
            {
                HumanTaskKey = firstTask.Key,
                Outcome = CompletionCondition.Reject.ToString(),
                ActorId = "asset-manager",
                ActorRoles = ["asset-manager"]
            });

            await WaitForAsync(async () =>
            {
                var current = await workflowStore.GetAsync(started.Key);
                var pending = await taskStore.GetPendingByWorkflowAsync(started.Key);
                return current?.StepIndex >= 2 || pending.Any(task => task.WorkflowStepId == "final-review");
            });

            var finalTasks = (await taskStore.GetPendingByWorkflowAsync(started.Key))
                .Where(task => task.WorkflowStepId == "final-review")
                .ToArray();

            // Independent business oracle B04: an initial rejection ends
            // maintenance and must not create a later approval task.
            finalTasks.Should().BeEmpty("an initial maintenance rejection must not produce a final approval task");
        }
        finally
        {
            for (var index = hostedServices.Length - 1; index >= 0; index--)
                await hostedServices[index].StopAsync(CancellationToken.None);
        }
    }

    private static HumanTaskDescriptor HumanTask(string id) => new()
    {
        Id = id,
        Name = id,
        Version = 1,
        State = DescriptorState.Active,
        Outcomes =
        [
            new CompletionOutcome { Condition = CompletionCondition.Approve },
            new CompletionOutcome { Condition = CompletionCondition.Reject }
        ]
    };

    private static async Task WaitForAsync(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            if (await predicate())
                return;
            await Task.Delay(20, timeout.Token);
        }

        throw new TimeoutException("The Workflow/HumanTask outbox continuation did not reach an observable state.");
    }

    private sealed class InlineDescriptorProvider<T>(params T[] descriptors) : IDescriptorProvider<T>
        where T : IDescriptor
    {
        public IReadOnlyList<T> GetDescriptors() => descriptors;
    }
}
