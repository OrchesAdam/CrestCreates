using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
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

    private static (WorkflowEngine engine, WorkflowContinuationService continuation,
        InMemoryWorkflowInstanceStore store) CreateServices(
        WorkflowRegistry registry, ICapabilityPipeline? pipeline = null)
    {
        var pipelineImpl = pipeline ?? new MockCapabilityPipeline(
            CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        var capExecutor = new CapabilityStepExecutor(pipelineImpl);
        var htExecutor = new HumanTaskStepExecutor();
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
        instance.WaitingHumanTaskId.Should().Be("ht_01");
        instance.StepIndex.Should().Be(1);
        instance.StepResults.Should().HaveCount(2);

        await continuation.ContinueAsync(new WorkflowContinuationRequest
            { HumanTaskId = "ht_01", Outcome = "Approved", Result = new { Score = 95 } });

        var final = await store.GetAsync(instance.InstanceId);
        final!.Status.Should().Be(WorkflowInstanceStatus.Completed);
        final.WaitingHumanTaskId.Should().BeNull();
        final.StepResults.Should().HaveCount(4);
        final.StepResults[3].Status.Should().Be(StepExecutionStatus.Completed);
    }

    [Fact]
    public async Task ContinueAsync_DoubleResume_ThrowsOnSecondCall()
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
        var (engine, continuation, _) = CreateServices(registry);

        await engine.ExecuteAsync("wf_01");
        await continuation.ContinueAsync(new WorkflowContinuationRequest
            { HumanTaskId = "ht_01", Outcome = "ok" });

        await continuation.Invoking(c => c.ContinueAsync(new WorkflowContinuationRequest
                { HumanTaskId = "ht_01", Outcome = "ok" }))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No suspended workflow instance*");
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
            { HumanTaskId = "ht_01", Outcome = "Approved", Result = new { Score = 95 } });

        var final = await store.GetAsync(instance.InstanceId);
        final!.Variables["lastStepOutcome"].Should().Be("Approved");
        final.StepResults.Should().HaveCount(2);
        final.StepResults[1].Status.Should().Be(StepExecutionStatus.Completed);
        final.StepResults[1].Output.Should().NotBeNull();
    }
}
