using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowEngineTests
{
    private static WorkflowDescriptor CreateWorkflow(string id, string name, int version,
        params WorkflowStep[] steps)
    {
        return new WorkflowDescriptor
        {
            Id = id, Name = name, Version = version, State = DescriptorState.Active,
            Steps = steps.ToList()
        };
    }

    private static WorkflowRegistry CreateRegistry(params WorkflowDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<WorkflowDescriptor>(
            Array.Empty<IRegistryValidator<WorkflowDescriptor>>());
        var registry = new WorkflowRegistry(engine);
        var provider = new TestWorkflowProvider(descriptors.ToList());
        registry.Build([provider]);
        return registry;
    }

    private class TestWorkflowProvider : IDescriptorProvider<WorkflowDescriptor>
    {
        private readonly List<WorkflowDescriptor> _descriptors;
        public TestWorkflowProvider(List<WorkflowDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<WorkflowDescriptor> GetDescriptors() => _descriptors;
    }

    private static WorkflowEngine CreateEngine(
        WorkflowRegistry registry,
        ICapabilityPipeline? pipeline = null)
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
                    Id = req.HumanTaskId,
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
        return new WorkflowEngine(registry, store, executionRunner, eventPublisher);
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

    [Fact]
    public async Task ExecuteAsync_WorkflowNotFound_Throws()
    {
        var registry = CreateRegistry();
        var engine = CreateEngine(registry);
        await engine.Invoking(e => e.ExecuteAsync("nonexistent"))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyWorkflow_CompletesImmediately()
    {
        var registry = CreateRegistry(CreateWorkflow("wf_01", "empty.wf", 1));
        var engine = CreateEngine(registry);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.StepResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_HumanTaskTarget_SuspendsInstance()
    {
        var registry = CreateRegistry(CreateWorkflow("wf_01", "suspend.wf", 1,
            new WorkflowStep
            {
                Id = "step_01", Name = "Human Step",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                }
            }));
        var engine = CreateEngine(registry);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.StepResults.Should().HaveCount(1);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Suspended);
    }

    [Fact]
    public async Task ExecuteAsync_StepsAfterHumanTask_NotExecuted()
    {
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "suspend2.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Human Step",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    }
                },
                new()
                {
                    Id = "step_02", Name = "Never Executed",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
                    }
                }
            }
        });
        var engine = CreateEngine(registry);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.StepResults.Should().HaveCount(1);
        instance.StepResults[0].StepId.Should().Be("step_01");
    }

    [Fact]
    public async Task ExecuteAsync_Variables_PassedAsInput()
    {
        var registry = CreateRegistry(CreateWorkflow("wf_01", "vars.wf", 1));
        var engine = CreateEngine(registry);

        var instance = await engine.ExecuteAsync("wf_01",
            new Dictionary<string, object?> { ["key1"] = "val1", ["key2"] = 42 });

        instance.Variables["key1"].Should().Be("val1");
        instance.Variables["key2"].Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_StepError_Skip_ContinuesAfterFailure()
    {
        var failingPipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Failure("ERR", "skip me", TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "skip.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Skipped Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
                    },
                    OnError = StepErrorBehavior.Skip
                },
                new()
                {
                    Id = "step_02", Name = "Good Step",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    }
                }
            }
        });
        var engine = CreateEngine(registry, pipeline: failingPipeline);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.StepResults.Should().HaveCount(2);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Failed);
        instance.StepResults[1].Status.Should().Be(StepExecutionStatus.Suspended);
    }

    [Fact]
    public async Task ExecuteAsync_StepError_Fail_StopsExecution()
    {
        var failingPipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Failure("ERR", "fail", TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "fail.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Failing Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
                    },
                    OnError = StepErrorBehavior.Fail
                },
                new()
                {
                    Id = "step_02", Name = "Never Reached",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    }
                }
            }
        });
        var engine = CreateEngine(registry, pipeline: failingPipeline);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Failed);
        instance.StepResults.Should().HaveCount(1);
    }
}
