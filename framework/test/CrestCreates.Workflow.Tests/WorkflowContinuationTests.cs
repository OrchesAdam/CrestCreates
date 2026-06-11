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
            WaitingHumanTaskId = "ht_01",
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
            { HumanTaskId = "ht_01", Outcome = "Approved" });

        var final = await store.GetAsync(instance.InstanceId);
        final!.Status.Should().Be(WorkflowInstanceStatus.Completed);
        final.StepResults.Select(r => r.StepId).Should().ContainInOrder(
            "v1_start", "v1_human", "v1_human", "v1_after");
        pipeline.CapabilityIds.Should().Equal("capability:cap_v1_after");
    }

    [Fact]
    public async Task GetByWaitingHumanTaskId_WhenDuplicateSuspendedInstancesExist_ThrowsCorrelationException()
    {
        var store = new InMemoryWorkflowInstanceStore();
        await store.SaveAsync(new WorkflowInstance
        {
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_01", 1),
            Status = WorkflowInstanceStatus.Suspended,
            WaitingHumanTaskId = "ht_01"
        });
        await store.SaveAsync(new WorkflowInstance
        {
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_02", 1),
            Status = WorkflowInstanceStatus.Suspended,
            WaitingHumanTaskId = "ht_01"
        });

        await store.Invoking(s => s.GetByWaitingHumanTaskId("ht_01"))
            .Should().ThrowAsync<WorkflowCorrelationException>()
            .WithMessage("*Multiple suspended instances*");
    }
}
