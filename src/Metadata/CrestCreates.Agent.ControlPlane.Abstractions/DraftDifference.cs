namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DraftDifference
{
    public required string Path { get; init; }
    public required string CurrentValue { get; init; }
    public required string ProposedValue { get; init; }
    public required DraftDifferenceKind Kind { get; init; }
}
