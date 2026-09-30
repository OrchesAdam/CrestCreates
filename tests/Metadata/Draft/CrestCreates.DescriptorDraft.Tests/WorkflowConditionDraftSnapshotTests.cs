using CrestCreates.DescriptorDraft;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DescriptorDraft.Tests;

public sealed class WorkflowConditionDraftSnapshotTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(WorkflowConditionTokens.PreviousHumanTaskApproved)]
    [InlineData(WorkflowConditionTokens.PreviousHumanTaskRejected)]
    public void Snapshot_RetainsNullableWorkflowCondition(string? condition)
    {
        var descriptor = new WorkflowDescriptor
        {
            Id = "workflow-maintenance",
            Name = "Maintenance",
            Version = 1,
            Steps =
            [
                new WorkflowStep
                {
                    Id = "initial-review",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_initial_review", 1)
                    }
                },
                new WorkflowStep
                {
                    Id = "final-review",
                    Condition = condition,
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_final_review", 1)
                    }
                }
            ]
        };

        var snapshot = new WorkflowDescriptorDraftPayload(descriptor).Snapshot()
            .Should().BeOfType<WorkflowDescriptorDraftPayload>().Subject;

        snapshot.Descriptor.Should().NotBeSameAs(descriptor);
        snapshot.Descriptor.Steps.Should().HaveCount(2);
        snapshot.Descriptor.Steps[1].Condition.Should().Be(condition);
        snapshot.Descriptor.Steps[1].Target.Should().NotBeSameAs(descriptor.Steps[1].Target);
    }
}
