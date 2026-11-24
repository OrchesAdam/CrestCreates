using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

#pragma warning disable CC1001
public static class AssetProductionDescriptors
{
    public const string WorkflowId = AssetContractIds.MaintenanceWorkflow;
    public const int WorkflowVersion = 3;
    public const string CriticalCategory = "CriticalEquipment";
    public const string TrialTaskId = "asset-maintenance-mes-trial";
    public const string QualityTaskId = "asset-maintenance-quality-release";
    public const string QualityRole = "asset-quality";
    public const string MesEvidenceActor = "asset-mes-evidence";
    public const string MesEvidenceRole = "asset-mes-evidence";
    public const string TrialStepId = "maintenance-mes-trial";
    public const string InitialStepId = "maintenance-initial-review";
    public const string QualityStepId = "maintenance-quality-release";
    public const string ReleaseCapabilityId = "asset-production-release";
    public const string ReleaseInputSchemaId = "asset-production-release-input";
    public const string ReleaseOutputSchemaId = "asset-production-release-output";

    public static SchemaDescriptor ReleaseInputSchema { get; } = new()
    {
        Id = ReleaseInputSchemaId, Name = ReleaseInputSchemaId, Version = 1, State = DescriptorState.Active,
        Fields =
        [
            new() { Name = "assetId", FieldType = "guid", IsRequired = true },
            new() { Name = "observationId", FieldType = "guid", IsRequired = true },
            new() { Name = "sourceEventId", FieldType = "guid", IsRequired = true },
            new() { Name = "workflowInstanceId", FieldType = "string", IsRequired = true, MinLength = 1, MaxLength = 128 }
        ]
    };

    public static SchemaDescriptor ReleaseOutputSchema { get; } = new()
    {
        Id = ReleaseOutputSchemaId, Name = ReleaseOutputSchemaId, Version = 1, State = DescriptorState.Active,
        Fields =
        [
            new() { Name = "operationId", FieldType = "guid", IsRequired = true },
            new() { Name = "receipt", FieldType = "object", IsRequired = true }
        ]
    };

    public static CapabilityDescriptor ReleaseCapability { get; } = new()
    {
        Id = ReleaseCapabilityId, Name = "Release critical equipment production lock", Version = 1,
        State = DescriptorState.Active, CapabilityKind = CapabilityKind.Command, RiskLevel = CapabilityRiskLevel.High,
        InputSchema = new VersionedDescriptorRef<SchemaDescriptor>(ReleaseInputSchemaId, 1),
        OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>(ReleaseOutputSchemaId, 1),
        Permissions = [AssetPermissions.Assets.ReleaseProduction]
    };

    public static HumanTaskDescriptor TrialTask { get; } = Task(TrialTaskId, "MES trial verification");
    public static HumanTaskDescriptor QualityTask { get; } = Task(QualityTaskId, "Independent quality sign-off");

    private static HumanTaskDescriptor Task(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Version = 1,
        State = DescriptorState.Active,
        Interaction = new VersionedDescriptorRef<IInteractionDescriptor>(AssetContractIds.MaintenanceForm, 1),
        AssigneeStrategy = AssigneeStrategy.CandidateGroup,
        Outcomes = [new() { Condition = CompletionCondition.Approve }, new() { Condition = CompletionCondition.Reject }]
    };

    public static void RequireProductionWorkflow(WorkflowDescriptor workflow)
    {
        if (workflow.Id != WorkflowId || workflow.Version != WorkflowVersion || workflow.State != DescriptorState.Active ||
            workflow.Steps is not [var initial, var trial, var quality] ||
            !Matches(initial, InitialStepId, AssetDescriptorCatalog.MaintenanceInitialHumanTask, null) ||
            !Matches(trial, TrialStepId, TrialTask, WorkflowConditionTokens.PreviousHumanTaskApproved) ||
            !Matches(quality, QualityStepId, QualityTask, "all-prior-human-tasks-approved"))
        {
            throw new InvalidOperationException("The production release requires supervisor review, MES evidence and independent quality sign-off in that order.");
        }
    }

    private static bool Matches(WorkflowStep step, string id, HumanTaskDescriptor task, string? condition) =>
        step.Id == id && step.Condition == condition && step.Target is HumanTaskTarget target &&
        target.HumanTask == new VersionedDescriptorRef<HumanTaskDescriptor>(task.Id, task.Version);
}
#pragma warning restore CC1001
