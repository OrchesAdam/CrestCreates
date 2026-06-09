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

    [Fact]
    public async Task ResumeAsync_NoDraftStore_Throws()
    {
        var registry = new WorkflowRegistry();
        registry.Register(CreateWorkflow("wf_01", "test.wf", 1));
        var engine = new WorkflowEngine(registry, draftStore: null);

        await engine.Invoking(e => e.ResumeAsync("instance_01"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IDraftStore*");
    }

    [Fact]
    public async Task ResumeAsync_NoCheckpoint_Throws()
    {
        var registry = new WorkflowRegistry();
        registry.Register(CreateWorkflow("wf_01", "test.wf", 1));
        var draftStore = new Draft.InMemoryDraftStore();
        var engine = new WorkflowEngine(registry, draftStore: draftStore);

        await engine.Invoking(e => e.ResumeAsync("instance_01"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*checkpoint*");
    }

    [Fact]
    public async Task ResumeAsync_ValidCheckpoint_ContinuesExecution()
    {
        var registry = new WorkflowRegistry();
        registry.Register(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "resume.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Already Done",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    }
                },
                new()
                {
                    Id = "step_02", Name = "Resume Here",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    }
                }
            }
        });

        var draftStore = new Draft.InMemoryDraftStore();
        var checkpointJson = System.Text.Json.JsonSerializer.Serialize(
            new WorkflowEngine.CheckpointState
            {
                InstanceId = "instance_01",
                WorkflowId = "wf_01",
                WorkflowVersion = 1,
                StepIndex = 1,
                CurrentStepId = "step_02"
            });

        await draftStore.SaveAsync(new Draft.Abstractions.DraftRecord
        {
            DraftId = "wf_ckpt_instance_01",
            DraftType = "workflow.checkpoint",
            Schema = new Schema.Abstractions.VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>("s", 1),
            TenantId = null,
            OwnerId = "instance_01",
            PayloadJson = checkpointJson
        });

        var engine = new WorkflowEngine(registry, draftStore: draftStore);
        var instance = await engine.ResumeAsync("instance_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.StepResults.Should().HaveCount(1);
        instance.StepResults[0].StepId.Should().Be("step_02");
    }

    [Fact]
    public async Task ResumeThenExecute_HasCorrectInstanceId()
    {
        var registry = new WorkflowRegistry();
        registry.Register(CreateWorkflow("wf_01", "resume2.wf", 1));
        var draftStore = new Draft.InMemoryDraftStore();
        var checkpointJson = System.Text.Json.JsonSerializer.Serialize(
            new WorkflowEngine.CheckpointState
            {
                InstanceId = "instance_02",
                WorkflowId = "wf_01",
                WorkflowVersion = 1,
                StepIndex = 0,
                CurrentStepId = null
            });

        await draftStore.SaveAsync(new Draft.Abstractions.DraftRecord
        {
            DraftId = "wf_ckpt_instance_02",
            DraftType = "workflow.checkpoint",
            Schema = new Schema.Abstractions.VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>("s", 1),
            TenantId = null,
            OwnerId = "instance_02",
            PayloadJson = checkpointJson
        });

        var engine = new WorkflowEngine(registry, draftStore: draftStore);
        var instance = await engine.ResumeAsync("instance_02");

        instance.InstanceId.Should().Be("instance_02");
        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
    }
}
