namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum FixProposalActionKind
{
    SetValue = 1,
    RemoveValue = 2,
    AddValue = 3,
    MergeObject = 4,
    ReplaceReference = 5,
    RemoveRelationship = 6,
    AddRequiredBindingMetadata = 7,
    SuggestVersionBump = 8,
    MarkRequiresReview = 9,
    ManualActionRequired = 10
}
