namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum FixProposalKind
{
    CreateMissingDescriptor = 1,
    ReplaceMissingReference = 2,
    RemoveInvalidRelationship = 3,
    AddRequiredBindingMetadata = 4,
    SplitBreakingChangeIntoCompatibleChange = 5,
    MarkRequiresReview = 6,
    FlagUnsafeExpansion = 7,
    SuggestVersionBump = 8,
    SetRequiredField = 9
}
