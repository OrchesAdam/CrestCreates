namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record ApplyFixProposalRequest
{
    public required string ProposalId { get; init; }
    public required string DraftId { get; init; }
}
