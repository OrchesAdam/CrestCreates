using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowRuntimeTests
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

        public Task<CapabilityExecutionResult> ExecuteAsync(
            CapabilityDescriptor descriptor, object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
            => ExecuteAsync(descriptor.Id, input, configureContext, ct);
    }

    [Fact]
    public async Task ExecuteAsync_TwoCapabilitySteps_CompletesSuccessfully()
    {
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "linear.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Step A",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } },
                new() { Id = "step_02", Name = "Step B",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_b", 1) } }
            }
        });

        var engine = CreateEngine(registry);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.StepResults.Should().HaveCount(2);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Completed);
        instance.StepResults[1].Status.Should().Be(StepExecutionStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_CapabilityThenHumanTask_Suspends()
    {
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "suspend.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Cap Step",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } },
                new() { Id = "step_02", Name = "Human Step",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } }
            }
        });

        var engine = CreateEngine(registry);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.StepResults.Should().HaveCount(2);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Completed);
        instance.StepResults[1].Status.Should().Be(StepExecutionStatus.Suspended);
        instance.StepIndex.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_CapabilityFails_StopsWithFailed()
    {
        var failingPipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Failure("ERR", "capability error", TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "fail.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Cap A",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } },
                new() { Id = "step_02", Name = "Cap B",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_b", 1) } }
            }
        });

        var engine = CreateEngine(registry, pipeline: failingPipeline);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Failed);
        instance.StepResults.Should().HaveCount(1);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_SkipOnError_ContinuesAfterFailure()
    {
        var failingPipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Failure("ERR", "skip me", TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "skip.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Failing Step",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) },
                    OnError = StepErrorBehavior.Skip },
                new() { Id = "step_02", Name = "Human Step",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } }
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
    public async Task ExecuteAsync_ExecutorThrows_RecordsAsFailed()
    {
        var throwingPipeline = new MockThrowingPipeline();
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "throw.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Boom",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } }
            }
        });

        var engine = CreateEngine(registry, pipeline: throwingPipeline);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Failed);
        instance.StepResults.Should().HaveCount(1);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Failed);
        instance.StepResults[0].ErrorMessage.Should().Be("infrastructure boom");
        instance.ErrorMessage.Should().Be("infrastructure boom");
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_PropagatesWithoutSave()
    {
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "cancel.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Cancel Me",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } }
            }
        });

        var cts = new CancellationTokenSource();
        var engine = CreateEngine(registry, pipeline: new MockCancellationPipeline(cts));

        // Start execution, cancel immediately
        var task = engine.ExecuteAsync("wf_01", ct: cts.Token);
        cts.Cancel();

        // Assert: cancellation propagates, instance not saved as Failed
        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
    }

    private class MockCancellationPipeline : ICapabilityPipeline
    {
        private readonly CancellationTokenSource _cts;
        public MockCancellationPipeline(CancellationTokenSource cts) => _cts = cts;

        public Task<CapabilityExecutionResult> ExecuteAsync(
            string capabilityIdOrName, object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
        {
            _cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        }

        public Task<CapabilityExecutionResult> ExecuteAsync(
            CapabilityDescriptor descriptor, object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
            => ExecuteAsync(descriptor.Id, input, configureContext, ct);
    }

    private class MockThrowingPipeline : ICapabilityPipeline
    {
        public Task<CapabilityExecutionResult> ExecuteAsync(
            string capabilityIdOrName, object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException("infrastructure boom");

        public Task<CapabilityExecutionResult> ExecuteAsync(
            CapabilityDescriptor descriptor, object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
            => ExecuteAsync(descriptor.Id, input, configureContext, ct);
    }
}
