using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class InteractionTargetTests
{
    [Fact]
    public void CapabilityTarget_References_CapabilityDescriptor()
    {
        var target = new CapabilityTarget
        {
            Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 3)
        };

        target.Capability.Id.Should().Be("cap_01");
        target.Capability.Version.Should().Be(3);
    }

    [Fact]
    public void HumanTaskTarget_References_HumanTaskDescriptor()
    {
        var target = new HumanTaskTarget
        {
            HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 2)
        };

        target.HumanTask.Id.Should().Be("ht_01");
        target.HumanTask.Version.Should().Be(2);
    }

    [Fact]
    public void SubWorkflowTarget_References_WorkflowDescriptor()
    {
        var target = new SubWorkflowTarget
        {
            SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_01", 1)
        };

        target.SubWorkflow.Id.Should().Be("wf_01");
        target.SubWorkflow.Version.Should().Be(1);
    }

    [Fact]
    public void All_Targets_Are_InteractionTarget()
    {
        var cap = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("c", 1) };
        var ht = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("h", 1) };
        var sw = new SubWorkflowTarget { SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>("w", 1) };

        (cap is InteractionTarget).Should().BeTrue();
        (ht is InteractionTarget).Should().BeTrue();
        (sw is InteractionTarget).Should().BeTrue();
    }
}
