namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum FixProposalApplicability
{
    CurrentMutableDraft = 1,
    RequiresNewDraftRevision = 2,
    ManualActionRequired = 3,
    NotApplicable = 4
}
