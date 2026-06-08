using CrestCreates.Capability.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskDescriptorTests
{
    [Fact]
    public void HumanTaskDescriptor_Kind_Is_HumanTask()
    {
        var task = new HumanTaskDescriptor
        {
            Id = "ht_01",
            Name = "manager.approval",
            Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1)
        };

        task.Kind.Should().Be(DescriptorKind.HumanTask);
    }

    [Fact]
    public void HumanTaskDescriptor_References_Form_By_VersionedRef()
    {
        var task = new HumanTaskDescriptor
        {
            Id = "ht_01",
            Name = "manager.approval",
            Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 3)
        };

        task.Form.Id.Should().Be("form_01");
        task.Form.Version.Should().Be(3);
    }

    [Fact]
    public void HumanTaskDescriptor_InputSchema_Is_Optional()
    {
        var task = new HumanTaskDescriptor
        {
            Id = "ht_01",
            Name = "simple.task",
            Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1)
        };

        task.InputSchema.Should().BeNull();
    }

    [Fact]
    public void HumanTaskDescriptor_Outcomes_Reference_Capability()
    {
        var task = new HumanTaskDescriptor
        {
            Id = "ht_01",
            Name = "manager.approval",
            Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1),
            Outcomes = new[]
            {
                new CompletionOutcome
                {
                    Condition = CompletionCondition.Approve,
                    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 2)
                },
                new CompletionOutcome
                {
                    Condition = CompletionCondition.Reject,
                    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_02", 1)
                }
            }
        };

        task.Outcomes.Should().HaveCount(2);
        task.Outcomes[0].Condition.Should().Be(CompletionCondition.Approve);
        task.Outcomes[0].Capability!.Value.Id.Should().Be("cap_01");
    }

    [Fact]
    public void HumanTaskDescriptor_AssigneeStrategy_Defaults_Correctly()
    {
        var task = new HumanTaskDescriptor
        {
            Id = "ht_01",
            Name = "simple.task",
            Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1),
            AssigneeStrategy = AssigneeStrategy.CandidateGroup
        };

        task.AssigneeStrategy.Should().Be(AssigneeStrategy.CandidateGroup);
    }

    [Fact]
    public void HumanTaskDescriptor_Timeout_Is_Optional()
    {
        var task = new HumanTaskDescriptor
        {
            Id = "ht_01",
            Name = "urgent.task",
            Version = 1,
            Form = new VersionedDescriptorRef<FormDescriptor>("form_01", 1),
            Timeout = TimeSpan.FromHours(24)
        };

        task.Timeout.Should().Be(TimeSpan.FromHours(24));
    }
}
