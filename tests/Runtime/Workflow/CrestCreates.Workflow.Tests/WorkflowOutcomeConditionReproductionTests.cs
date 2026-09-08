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

/// <summary>
/// B04 reproduction for the Phase 10c Asset scenario. This intentionally
/// Guards the production WorkflowStep.Condition routing contract for the
/// Phase 10c Asset scenario.
/// </summary>
public sealed class WorkflowOutcomeConditionReproductionTests
{
    [Theory]
    [InlineData(WorkflowConditionTokens.PreviousHumanTaskApproved, "Approve", true)]
    [InlineData(WorkflowConditionTokens.PreviousHumanTaskApproved, "Reject", false)]
    [InlineData(WorkflowConditionTokens.PreviousHumanTaskRejected, "Approve", false)]
    [InlineData(WorkflowConditionTokens.PreviousHumanTaskRejected, "Reject", true)]
    public async Task OutcomeConditionRoutesOnlyMatchingCanonicalOutcome(
        string condition, string outcome, bool expectFinalTask)
        => await RunScenarioAsync(outcome, expectFinalTask, condition);

    [Fact]
    public async Task MalformedOutcome_FailsRunnerPersistsFailureAndAccountability()
    {
        var initialReview = HumanTask("ht_asset_malformed_initial_review");
        var finalReview = HumanTask("ht_asset_malformed_final_review");
        var workflow = new WorkflowDescriptor
        {
            Id = "wf_asset_malformed_outcome_reproduction",
            Name = "Asset malformed outcome reproduction",
            Version = 1,
            State = DescriptorState.Active,
            Steps =
            [
                new WorkflowStep
                {
                    Id = "initial-review",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(initialReview.Id, 1)
                    }
                },
                new WorkflowStep
                {
                    Id = "final-review",
                    Condition = WorkflowConditionTokens.PreviousHumanTaskApproved,
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(finalReview.Id, 1)
                    }
                }
            ]
        };
        var auditSink = new InMemoryAuditSink();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, DefaultLocalEventBus>();
        services.AddAccountability();
        services.AddSingleton(auditSink);
        services.AddSingleton<IAuditSink>(auditSink);
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
            var workflowStore = serviceProvider.GetRequiredService<IWorkflowInstanceStore>();
            var stateRegistry = serviceProvider.GetRequiredService<IRuntimeStateContractRegistry>();
            var started = await engine.ExecuteAsync(new WorkflowExecutionRequest
            {
                WorkflowId = workflow.Id,
                TenantId = "asset-tenant"
            });

            var malformed = (await workflowStore.GetAsync(started.Key))!;
            malformed.Status = WorkflowInstanceStatus.Running;
            malformed.StepIndex = 1;
            malformed.CurrentStepId = "final-review";
            malformed.WaitingHumanTaskKey = null;
            malformed.StepResults.Add(new WorkflowStepResult
            {
                StepId = "initial-review",
                StepName = "Initial review",
                Status = StepExecutionStatus.Completed,
                ExecutedAt = DateTimeOffset.UtcNow
            });
            malformed.Variables["lastStepOutcome"] = stateRegistry.Capture(42);
            await workflowStore.UpdateAsync(malformed, malformed.Revision);

            var runner = serviceProvider.GetRequiredService<IWorkflowExecutionRunner>();
            var result = await runner.RunAsync(
                (await workflowStore.GetAsync(started.Key))!,
                "malformed-outcome-run",
                null,
                CancellationToken.None);

            result.Status.Should().Be(WorkflowInstanceStatus.Failed);
            result.StepResults.Should().Contain(step =>
                step.StepId == "final-review" && step.Status == StepExecutionStatus.Failed);
            result.ErrorMessage.Should().Contain("malformed persisted lastStepOutcome");

            var persisted = await workflowStore.GetAsync(started.Key);
            persisted.Should().NotBeNull();
            persisted!.Status.Should().Be(WorkflowInstanceStatus.Failed);
            persisted.LastLifecycleAuditId.Should().NotBeNullOrWhiteSpace();

            await WaitForAsync(async () => auditSink.GetRecords().Any(record =>
                record.Action.Name == "workflow.failed"
                && record.Outcome.Status == "failed"));
        }
        finally
        {
            for (var index = hostedServices.Length - 1; index >= 0; index--)
                await hostedServices[index].StopAsync(CancellationToken.None);
        }
    }

    private static async Task RunScenarioAsync(
        string outcome,
        bool expectFinalTask,
        string condition = WorkflowConditionTokens.PreviousHumanTaskApproved)
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
                    Condition = condition,
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(finalReview.Id, 1)
                    }
                }
            ]
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, DefaultLocalEventBus>();
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
                Outcome = outcome,
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

            if (expectFinalTask)
                finalTasks.Should().ContainSingle("an initial approval must produce exactly one final approval task");
            else
            {
                finalTasks.Should().BeEmpty("an initial maintenance rejection must not produce a final approval task");
                var completed = await workflowStore.GetAsync(started.Key);
                completed.Should().NotBeNull();
                completed!.StepResults.Should().Contain(result =>
                    result.StepId == "final-review" && result.Status == StepExecutionStatus.Skipped);
            }
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
