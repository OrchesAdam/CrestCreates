using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Sample.AssetManagement.Host;

#pragma warning disable CC1001
/// <summary>
/// Candidate-only descriptors for the terminating Asset v2 verification
/// profile. The ordinary Host never registers this descriptor.
/// </summary>
public static class AssetCandidateDescriptorCatalog
{
    public static WorkflowDescriptor MaintenanceWorkflow { get; } = new()
    {
        Id = AssetContractIds.MaintenanceWorkflow, Name = "Asset maintenance review", Version = 2, State = DescriptorState.Active,
        Steps =
        [
            new WorkflowStep
            {
                Id = "maintenance-initial-review",
                Name = "Manager initial maintenance review",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(AssetContractIds.MaintenanceInitialHumanTask, 1)
                }
            },
            new WorkflowStep
            {
                Id = "maintenance-final-review",
                Name = "Manager final maintenance review",
                Condition = WorkflowConditionTokens.PreviousHumanTaskApproved,
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(AssetContractIds.MaintenanceHumanTask, 1)
                }
            }
        ]
    };
}
#pragma warning restore CC1001
