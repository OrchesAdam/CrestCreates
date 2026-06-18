namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record FixProposalAction
{
    public required string Path { get; init; }
    public required FixProposalActionKind ActionKind { get; init; }
    public required string CurrentValue { get; init; }
    public required string ProposedValue { get; init; }
    public string? Description { get; init; }
}
