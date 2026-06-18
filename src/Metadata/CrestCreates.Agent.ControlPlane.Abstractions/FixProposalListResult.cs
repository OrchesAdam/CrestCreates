namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record FixProposalListResult
{
    public required IReadOnlyList<FixProposal> Proposals { get; init; }
}
