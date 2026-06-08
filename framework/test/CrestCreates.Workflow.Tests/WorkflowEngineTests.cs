using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
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

    [Fact]
    public async Task ExecuteAsync_WorkflowNotFound_Throws()
    {
        var registry = new WorkflowRegistry();
        var engine = new WorkflowEngine(registry);
        await engine.Invoking(e => e.ExecuteAsync("nonexistent"))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyWorkflow_CompletesImmediately()
    {
        var registry = new WorkflowRegistry();
        registry.Register(CreateWorkflow("wf_01", "empty.wf", 1));
        var engine = new WorkflowEngine(registry);

        var instance = await engine.ExecuteAsync("empty.wf");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.StepResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTarget_ReturnsFailure()
    {
        var registry = new WorkflowRegistry();
        registry.Register(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "unknown.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Bad Step",
                    Target = new TestTarget()
                }
            }
        });
        var engine = new WorkflowEngine(registry);

        var instance = await engine.ExecuteAsync("unknown.wf");

        instance.Status.Should().Be(WorkflowInstanceStatus.Failed);
        instance.StepResults[0].IsSuccess.Should().BeFalse();
        instance.StepResults[0].ErrorMessage.Should().Contain("Unknown target");
    }

    private sealed record TestTarget : InteractionTarget;

    [Fact]
    public async Task ExecuteAsync_CapabilityTarget_NoPipeline_ReturnsFailure()
    {
        var registry = new WorkflowRegistry();
        registry.Register(CreateWorkflow("wf_01", "cap.wf", 1,
            new WorkflowStep
            {
                Id = "step_01", Name = "Cap Step",
                Target = new CapabilityTarget
                {
                    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
                }
            }));
        var engine = new WorkflowEngine(registry, pipeline: null);

        var instance = await engine.ExecuteAsync("cap.wf");

        instance.Status.Should().Be(WorkflowInstanceStatus.Failed);
        instance.StepResults[0].ErrorMessage.Should().Contain("No ICapabilityPipeline");
    }

    [Fact]
    public async Task ExecuteAsync_HumanTaskTarget_SucceedsAsPassthrough()
    {
        var registry = new WorkflowRegistry();
        registry.Register(CreateWorkflow("wf_01", "ht.wf", 1,
            new WorkflowStep
            {
                Id = "step_01", Name = "Human Step",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                }
            }));
        var engine = new WorkflowEngine(registry);

        var instance = await engine.ExecuteAsync("ht.wf");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.StepResults[0].IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SubWorkflow_ExecutesRecursively()
    {
        var registry = new WorkflowRegistry();
        registry.Register(CreateWorkflow("wf_01", "parent.wf", 1,
            new WorkflowStep
            {
                Id = "step_01", Name = "Sub",
                Target = new SubWorkflowTarget
                {
                    SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_02", 1)
                }
            }));
        registry.Register(CreateWorkflow("wf_02", "child.wf", 1));
        var engine = new WorkflowEngine(registry);

        var instance = await engine.ExecuteAsync("parent.wf");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.StepResults.Should().HaveCount(1);
        instance.StepResults[0].IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Variables_PassedAsInput()
    {
        var registry = new WorkflowRegistry();
        registry.Register(CreateWorkflow("wf_01", "vars.wf", 1));
        var engine = new WorkflowEngine(registry);

        var instance = await engine.ExecuteAsync("vars.wf",
            new Dictionary<string, object?> { ["key1"] = "val1", ["key2"] = 42 });

        instance.Variables["key1"].Should().Be("val1");
        instance.Variables["key2"].Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_StepError_Retry_HasMaxRetryGuard()
    {
        var registry = new WorkflowRegistry();
        registry.Register(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "retry.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Failing Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
                    },
                    OnError = StepErrorBehavior.Retry
                }
            }
        });
        var engine = new WorkflowEngine(registry, pipeline: null);

        var instance = await engine.ExecuteAsync("retry.wf");

        // Retry max 3 times, then fails
        instance.Status.Should().Be(WorkflowInstanceStatus.Failed);
        instance.StepResults.Should().HaveCount(4);
        instance.StepResults.All(r => !r.IsSuccess).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_StepError_Skip_ContinuesToNext()
    {
        var registry = new WorkflowRegistry();
        registry.Register(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "skip.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Skipped Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
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
        var engine = new WorkflowEngine(registry, pipeline: null);

        var instance = await engine.ExecuteAsync("skip.wf");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.StepResults.Should().HaveCount(2);
        instance.StepResults[0].IsSuccess.Should().BeFalse();
        instance.StepResults[1].IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_StepTransition_FollowsSpecifiedStep()
    {
        var registry = new WorkflowRegistry();
        registry.Register(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "transition.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "First",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    },
                    Transitions = new List<string> { "step_03" }
                },
                new()
                {
                    Id = "step_02", Name = "Skipped",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    }
                },
                new()
                {
                    Id = "step_03", Name = "Target",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    }
                }
            }
        });
        var engine = new WorkflowEngine(registry);

        var instance = await engine.ExecuteAsync("transition.wf");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.StepResults.Should().HaveCount(2);
        instance.StepResults[0].StepId.Should().Be("step_01");
        instance.StepResults[1].StepId.Should().Be("step_03");
    }

    [Fact]
    public async Task ExecuteAsync_StepError_Fail_StopsExecution()
    {
        var registry = new WorkflowRegistry();
        registry.Register(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "fail.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Failing Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
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
        var engine = new WorkflowEngine(registry, pipeline: null);

        var instance = await engine.ExecuteAsync("fail.wf");

        instance.Status.Should().Be(WorkflowInstanceStatus.Failed);
        instance.StepResults.Should().HaveCount(1);
    }
}
