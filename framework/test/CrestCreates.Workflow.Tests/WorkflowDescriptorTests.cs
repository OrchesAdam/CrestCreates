using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowDescriptorTests
{
    [Fact]
    public void WorkflowDescriptor_Kind_Is_Workflow()
    {
        var wf = new WorkflowDescriptor
        {
            Id = "wf_01",
            Name = "employee.onboarding",
            Version = 1
        };

        wf.Kind.Should().Be(DescriptorKind.Workflow);
    }

    [Fact]
    public void WorkflowDescriptor_Steps_Contain_Targets()
    {
        var wf = new WorkflowDescriptor
        {
            Id = "wf_01",
            Name = "employee.onboarding",
            Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step_01",
                    Name = "Create Customer",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
                    },
                    OnError = StepErrorBehavior.Compensate
                },
                new WorkflowStep
                {
                    Id = "step_02",
                    Name = "Manager Approval",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    },
                    Transitions = new[] { "step_03" }
                }
            }
        };

        wf.Steps.Should().HaveCount(2);
        wf.Steps[0].Target.Should().BeOfType<CapabilityTarget>();
        wf.Steps[0].OnError.Should().Be(StepErrorBehavior.Compensate);
        wf.Steps[1].Transitions.Should().Contain("step_03");
    }

    [Fact]
    public void WorkflowDescriptor_VariableSchema_Is_Optional()
    {
        var wf = new WorkflowDescriptor
        {
            Id = "wf_01",
            Name = "simple.wf",
            Version = 1
        };

        wf.VariableSchema.Should().BeNull();
    }

    [Fact]
    public void WorkflowDescriptor_VariableSchema_Can_Be_Set()
    {
        var wf = new WorkflowDescriptor
        {
            Id = "wf_01",
            Name = "employee.onboarding",
            Version = 1,
            VariableSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 2)
        };

        wf.VariableSchema!.Value.Id.Should().Be("schema_01");
        wf.VariableSchema!.Value.Version.Should().Be(2);
    }

    [Fact]
    public void WorkflowDraftPolicy_Defaults()
    {
        var policy = new WorkflowDraftPolicy
        {
            EnableCheckpointing = true
        };

        policy.EnableCheckpointing.Should().BeTrue();
        policy.SaveInterval.Should().Be(TimeSpan.FromMinutes(5));
        policy.SaveBeforeHumanTask.Should().BeTrue();
        policy.SaveBeforeSubWorkflow.Should().BeTrue();
    }

    [Fact]
    public void WorkflowStep_Id_Survives_Reordering()
    {
        var stepId = "step_01JMXZ8K";

        var step = new WorkflowStep
        {
            Id = stepId,
            Name = "Some Step",
            Target = new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
            }
        };

        step.Id.Should().Be(stepId);
    }
}
