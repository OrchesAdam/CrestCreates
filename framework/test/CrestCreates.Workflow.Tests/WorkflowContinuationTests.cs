using CrestCreates.Capability.Abstractions;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowContinuationTests
{
    private static WorkflowRegistry CreateRegistry(params WorkflowDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<WorkflowDescriptor>(
            Array.Empty<IRegistryValidator<WorkflowDescriptor>>());
        var registry = new WorkflowRegistry(engine);
        registry.Build([new TestWorkflowProvider(descriptors.ToList())]);
        return registry;
    }

    private class TestWorkflowProvider : IDescriptorProvider<WorkflowDescriptor>
    {
        private readonly List<WorkflowDescriptor> _descriptors;
        public TestWorkflowProvider(List<WorkflowDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<WorkflowDescriptor> GetDescriptors() => _descriptors;
    }

    private class MockCapabilityPipeline : ICapabilityPipeline
    {
        private readonly CapabilityExecutionResult _result;
        public MockCapabilityPipeline(CapabilityExecutionResult result) => _result = result;
        public Task<CapabilityExecutionResult> ExecuteAsync(
            string capabilityIdOrName, object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class CapturingCapabilityPipeline : ICapabilityPipeline
    {
        public List<string> CapabilityIds { get; } = new();

        public Task<CapabilityExecutionResult> ExecuteAsync(
            string capabilityIdOrName, object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
        {
            CapabilityIds.Add(capabilityIdOrName);
            return Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        }
    }

    private sealed class WorkflowTestServices : IDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required IServiceScope Scope { get; init; }
        public required IWorkflowEngine Engine { get; init; }
        public required IHumanTaskRuntime HumanTaskRuntime { get; init; }
        public required IWorkflowInstanceStore WorkflowStore { get; init; }
        public required IHumanTaskInstanceStore HumanTaskStore { get; init; }

        public void Dispose()
        {
            Scope.Dispose();
            Provider.Dispose();
        }
    }

    private static (WorkflowEngine engine, WorkflowContinuationService continuation,
        InMemoryWorkflowInstanceStore store) CreateServices(
        WorkflowRegistry registry, ICapabilityPipeline? pipeline = null)
    {
        var pipelineImpl = pipeline ?? new MockCapabilityPipeline(
            CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        var capExecutor = new CapabilityStepExecutor(pipelineImpl);
        var mockRuntime = new Mock<IHumanTaskRuntime>();
        mockRuntime
            .Setup(r => r.CreateAsync(It.IsAny<HumanTaskCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HumanTaskCreationRequest req, CancellationToken _) =>
                new HumanTaskInstance
                {
                    Id = $"inst-{req.HumanTaskId}",
                    HumanTaskId = req.HumanTaskId,
                    HumanTaskVersion = req.Version ?? 1
                });
        var htExecutor = new HumanTaskStepExecutor(mockRuntime.Object);
        var executorRegistry = new DefaultStepExecutorRegistry(capExecutor, htExecutor);
        var store = new InMemoryWorkflowInstanceStore();
        var stateMachine = new DefaultWorkflowStateMachine();
        var eventPublisher = new WorkflowLifecycleEventPublisher();
        var executionRunner = new WorkflowExecutionRunner(
            registry, executorRegistry, store, stateMachine, eventPublisher);
        var engine = new WorkflowEngine(registry, store, executionRunner, eventPublisher);
        var continuation = new WorkflowContinuationService(
            store, stateMachine, registry, executionRunner, eventPublisher);
        return (engine, continuation, store);
    }

    [Fact]
    public async Task FullLoop_ExecuteSuspendContinue_CompletesSuccessfully()
    {
        var pipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Success(
                new Dictionary<string, object?> { ["output_key"] = "output_val" }, TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "loop.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Cap A",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } },
                new() { Id = "step_02", Name = "Approval",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } },
                new() { Id = "step_03", Name = "Cap B",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_b", 1) } }
            }
        });
        var (engine, continuation, store) = CreateServices(registry, pipeline);

        var instance = await engine.ExecuteAsync("wf_01");
        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.WaitingHumanTaskId.Should().Be("inst-ht_01");
        instance.StepIndex.Should().Be(1);
        instance.StepResults.Should().HaveCount(2);

        await continuation.ContinueAsync(new WorkflowContinuationRequest
            { HumanTaskId = instance.WaitingHumanTaskId!, Outcome = "Approved", Result = new { Score = 95 } });

        var final = await store.GetAsync(instance.InstanceId);
        final!.Status.Should().Be(WorkflowInstanceStatus.Completed);
        final.WaitingHumanTaskId.Should().BeNull();
        final.StepResults.Should().HaveCount(4);
        final.StepResults[3].Status.Should().Be(StepExecutionStatus.Completed);
    }

    [Fact]
    public async Task ContinueAsync_DoubleResume_DoesNotThrow_OnSecondCall()
    {
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "double.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Approval",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } }
            }
        });
        var (engine, continuation, store) = CreateServices(registry);

        var instance = await engine.ExecuteAsync("wf_01");
        await continuation.ContinueAsync(new WorkflowContinuationRequest
            { HumanTaskId = instance.WaitingHumanTaskId!, Outcome = "ok" });

        var secondAct = () => continuation.ContinueAsync(new WorkflowContinuationRequest
            { HumanTaskId = instance.WaitingHumanTaskId!, Outcome = "ok" });
        await secondAct.Should().NotThrowAsync();

        var final = await store.GetAsync(instance.InstanceId);
        final!.StepResults.Should().HaveCount(2);
        final.Status.Should().Be(WorkflowInstanceStatus.Completed);
        final.WaitingHumanTaskId.Should().BeNull();
    }

    [Fact]
    public async Task ContinueAsync_VariablesAndStepResult_Propagated()
    {
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "vars.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Approval",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } }
            }
        });
        var (engine, continuation, store) = CreateServices(registry);

        var instance = await engine.ExecuteAsync("wf_01");
        await continuation.ContinueAsync(new WorkflowContinuationRequest
            { HumanTaskId = instance.WaitingHumanTaskId!, Outcome = "Approved", Result = new { Score = 95 } });

        var final = await store.GetAsync(instance.InstanceId);
        final!.Variables["lastStepOutcome"].Should().Be("Approved");
        final.StepResults.Should().HaveCount(2);
        final.StepResults[1].Status.Should().Be(StepExecutionStatus.Completed);
        final.StepResults[1].Output.Should().NotBeNull();
    }

    [Fact]
    public async Task ContinueAsync_WhenLatestWorkflowVersionDiffers_ResumesOriginalInstanceVersion()
    {
        var pipeline = new CapturingCapabilityPipeline();
        var registry = CreateRegistry(
            new WorkflowDescriptor
            {
                Id = "wf_01", Name = "versioned.wf", Version = 1, State = DescriptorState.Active,
                Steps = new List<WorkflowStep>
                {
                    new() { Id = "v1_start", Name = "V1 Start",
                        Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_v1_start", 1) } },
                    new() { Id = "v1_human", Name = "V1 Approval",
                        Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } },
                    new() { Id = "v1_after", Name = "V1 After",
                        Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_v1_after", 1) } }
                }
            },
            new WorkflowDescriptor
            {
                Id = "wf_01", Name = "versioned.wf", Version = 2, State = DescriptorState.Active,
                Steps = new List<WorkflowStep>
                {
                    new() { Id = "v2_start", Name = "V2 Start",
                        Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_v2_start", 1) } },
                    new() { Id = "v2_human", Name = "V2 Approval",
                        Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } },
                    new() { Id = "v2_after", Name = "V2 After",
                        Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_v2_after", 1) } }
                }
            });
        var (_, continuation, store) = CreateServices(registry, pipeline);
        var instance = new WorkflowInstance
        {
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_01", 1),
            Status = WorkflowInstanceStatus.Suspended,
            StepIndex = 1,
            WaitingHumanTaskId = "inst-versioned-01",
            StepResults =
            {
                new WorkflowStepResult
                {
                    StepId = "v1_start",
                    StepName = "V1 Start",
                    Status = StepExecutionStatus.Completed,
                    ExecutedAt = DateTimeOffset.UtcNow
                },
                new WorkflowStepResult
                {
                    StepId = "v1_human",
                    StepName = "V1 Approval",
                    Status = StepExecutionStatus.Suspended,
                    ExecutedAt = DateTimeOffset.UtcNow
                }
            }
        };
        await store.SaveAsync(instance);

        await continuation.ContinueAsync(new WorkflowContinuationRequest
            { HumanTaskId = "inst-versioned-01", Outcome = "Approved" });

        var final = await store.GetAsync(instance.InstanceId);
        final!.Status.Should().Be(WorkflowInstanceStatus.Completed);
        final.StepResults.Select(r => r.StepId).Should().ContainInOrder(
            "v1_start", "v1_human", "v1_human", "v1_after");
        pipeline.CapabilityIds.Should().Equal("cap_v1_after");
    }

    [Fact]
    public async Task GetByWaitingHumanTaskId_WhenDuplicateSuspendedInstancesExist_ThrowsCorrelationException()
    {
        var store = new InMemoryWorkflowInstanceStore();
        await store.SaveAsync(new WorkflowInstance
        {
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_01", 1),
            Status = WorkflowInstanceStatus.Suspended,
            WaitingHumanTaskId = "inst-dup"
        });
        await store.SaveAsync(new WorkflowInstance
        {
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_02", 1),
            Status = WorkflowInstanceStatus.Suspended,
            WaitingHumanTaskId = "inst-dup"
        });

        await store.Invoking(s => s.GetByWaitingHumanTaskId("inst-dup"))
            .Should().ThrowAsync<WorkflowCorrelationException>()
            .WithMessage("*Multiple suspended instances*");
    }

    [Fact]
    public async Task HumanTaskStepExecutor_Creates_Instance_And_Returns_Suspended()
    {
        var mockRuntime = new Mock<IHumanTaskRuntime>();
        mockRuntime
            .Setup(r => r.CreateAsync(
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((HumanTaskCreationRequest req, CancellationToken _) =>
                new HumanTaskInstance
                {
                    Id = "inst-001",
                    HumanTaskId = req.HumanTaskId,
                    HumanTaskVersion = 1,
                    Status = HumanTaskInstanceStatus.Created,
                    WorkflowInstanceId = req.WorkflowInstanceId,
                    WorkflowStepId = req.WorkflowStepId,
                    Input = req.Input
                });

        var executor = new HumanTaskStepExecutor(mockRuntime.Object);

        var descriptor = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1,
            State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Approval",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    }
                }
            }
        };
        var instance = new WorkflowInstance
        {
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_01", 1),
            Variables = { ["request"] = "test-data" }
        };

        var context = new WorkflowExecutionContext(descriptor, instance, descriptor.Steps[0]);
        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        result.Status.Should().Be(StepExecutionStatus.Suspended);
        result.WaitingHumanTaskId.Should().Be("inst-001");

        mockRuntime.Verify(
            r => r.CreateAsync(
                It.Is<HumanTaskCreationRequest>(req =>
                    req.HumanTaskId == "ht_01" &&
                    req.WorkflowInstanceId == instance.InstanceId &&
                    req.WorkflowStepId == "step_01" &&
                    req.Input != null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Workflow_HumanTask_EndToEnd_Event_Completion_Resumes_Workflow()
    {
        var htDescriptor = new HumanTaskDescriptor
        {
            Id = "ht_01", Name = "approval.task", Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1),
            Outcomes = new[]
            {
                new CompletionOutcome { Condition = CompletionCondition.Approve },
                new CompletionOutcome { Condition = CompletionCondition.Reject }
            }
        };
        var htValidationEngine = new RegistryValidationEngine<HumanTaskDescriptor>(
            Array.Empty<IRegistryValidator<HumanTaskDescriptor>>());
        var htRegistry = new HumanTaskRegistry(htValidationEngine);
        htRegistry.Build([new TestHumanTaskDescriptorProvider([htDescriptor])]);

        var pipeline = new CapturingCapabilityPipeline();
        var wfRegistry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "e2e.wf", Version = 1,
            State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Approval",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    }
                },
                new()
                {
                    Id = "step_02", Name = "PostApproval",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_post", 1)
                    }
                }
            }
        });

        using var services = CreateComposedServices(wfRegistry, htRegistry, pipeline);

        var instance = await services.Engine.ExecuteAsync("wf_01");
        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.WaitingHumanTaskId.Should().NotBeNullOrEmpty();
        instance.StepIndex.Should().Be(0);

        var humanTaskInstance = await services.HumanTaskStore.GetByIdAsync(instance.WaitingHumanTaskId!);
        humanTaskInstance.Should().NotBeNull();
        humanTaskInstance!.WorkflowInstanceId.Should().Be(instance.InstanceId);

        await services.HumanTaskRuntime.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = humanTaskInstance.Id,
            Outcome = "Approve",
            Result = new { Score = 95 }
        });

        var final = await services.WorkflowStore.GetAsync(instance.InstanceId);
        final!.Status.Should().Be(WorkflowInstanceStatus.Completed);
        final.WaitingHumanTaskId.Should().BeNull();
        final.StepResults.Should().HaveCountGreaterThanOrEqualTo(3);
        final.Variables["lastStepOutcome"].Should().Be("Approve");
    }

    private static WorkflowTestServices CreateComposedServices(
        WorkflowRegistry workflowRegistry,
        HumanTaskRegistry humanTaskRegistry,
        ICapabilityPipeline capabilityPipeline)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowRegistry>(workflowRegistry);
        services.AddSingleton<IHumanTaskRegistry>(humanTaskRegistry);
        services.AddSingleton(capabilityPipeline);
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddHumanTaskRuntime();
        services.AddWorkflowEngine();

        var provider = services.BuildServiceProvider(validateScopes: true);
        var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;

        return new WorkflowTestServices
        {
            Provider = provider,
            Scope = scope,
            Engine = scoped.GetRequiredService<IWorkflowEngine>(),
            HumanTaskRuntime = scoped.GetRequiredService<IHumanTaskRuntime>(),
            WorkflowStore = scoped.GetRequiredService<IWorkflowInstanceStore>(),
            HumanTaskStore = scoped.GetRequiredService<IHumanTaskInstanceStore>()
        };
    }

    [Fact]
    public async Task WorkflowContinuation_DuplicateHumanTaskCompletedEvent_DoesNotDoubleAdvance()
    {
        // Build workflow with one HumanTask step followed by one Capability step
        var htDescriptor = new HumanTaskDescriptor
        {
            Id = "ht_dup", Name = "dup.task", Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1),
            Outcomes = new[]
            {
                new CompletionOutcome { Condition = CompletionCondition.Approve }
            }
        };
        var htValidationEngine = new RegistryValidationEngine<HumanTaskDescriptor>(
            Array.Empty<IRegistryValidator<HumanTaskDescriptor>>());
        var htRegistry = new HumanTaskRegistry(htValidationEngine);
        htRegistry.Build([new TestHumanTaskDescriptorProvider([htDescriptor])]);

        var htStore = new InMemoryHumanTaskInstanceStore();
        var htRuntime = new DefaultHumanTaskRuntime(htRegistry, htStore, NullLocalEventBus.Instance,
            new DefaultHumanTaskAssigneeResolver());

        var pipeline = new CapturingCapabilityPipeline();
        var wfRegistry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_dup", Name = "dup.wf", Version = 1,
            State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Approval",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_dup", 1)
                    }
                },
                new()
                {
                    Id = "step_02", Name = "PostApproval",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_post", 1)
                    }
                }
            }
        });

        var wfStore = new InMemoryWorkflowInstanceStore();
        var stateMachine = new DefaultWorkflowStateMachine();
        var eventPublisher = new WorkflowLifecycleEventPublisher();
        var capExecutor = new CapabilityStepExecutor(pipeline);
        var htExecutor = new HumanTaskStepExecutor(htRuntime);
        var executorRegistry = new DefaultStepExecutorRegistry(capExecutor, htExecutor);
        var executionRunner = new WorkflowExecutionRunner(
            wfRegistry, executorRegistry, wfStore, stateMachine, eventPublisher);
        var engine = new WorkflowEngine(wfRegistry, wfStore, executionRunner, eventPublisher);
        var continuation = new WorkflowContinuationService(
            wfStore, stateMachine, wfRegistry, executionRunner, eventPublisher);

        // Start workflow → suspends at HumanTask step (step_01, index 0)
        var instance = await engine.ExecuteAsync("wf_dup");
        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.StepIndex.Should().Be(0);

        // Find the HumanTaskInstance
        var humanTaskInstance = await htStore.GetByIdAsync(instance.WaitingHumanTaskId!);
        humanTaskInstance.Should().NotBeNull();
        var htInstanceId = humanTaskInstance!.Id;

        // Complete the HumanTask
        await htRuntime.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = htInstanceId,
            Outcome = "Approve"
        });

        // First continuation — should advance
        await continuation.ContinueAsync(new WorkflowContinuationRequest
        {
            HumanTaskId = htInstanceId,
            Outcome = "Approve"
        });
        var afterFirst = await wfStore.GetAsync(instance.InstanceId);
        afterFirst.Should().NotBeNull();
        var stepResultsAfterFirst = afterFirst!.StepResults.Count;

        // Second continuation with same instanceId — should be no-op (return, no exception)
        await continuation.Invoking(c => c.ContinueAsync(new WorkflowContinuationRequest
        {
            HumanTaskId = htInstanceId,
            Outcome = "Approve"
        })).Should().NotThrowAsync();

        var afterSecond = await wfStore.GetAsync(instance.InstanceId);
        afterSecond.Should().NotBeNull();
        // StepResults count unchanged — no double advance
        afterSecond!.StepResults.Should().HaveCount(stepResultsAfterFirst);
    }

    private class TestHumanTaskDescriptorProvider : IDescriptorProvider<HumanTaskDescriptor>
    {
        private readonly List<HumanTaskDescriptor> _descriptors;
        public TestHumanTaskDescriptorProvider(List<HumanTaskDescriptor> descriptors)
            => _descriptors = descriptors;
        public IReadOnlyList<HumanTaskDescriptor> GetDescriptors() => _descriptors;
    }

    private sealed class NullLocalEventBus : ILocalEventBus
    {
        public static readonly NullLocalEventBus Instance = new();
        public Task PublishAsync(ILocalEvent @event, CancellationToken ct = default) => Task.CompletedTask;
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : ILocalEvent => Task.CompletedTask;
    }
}
