namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record ReviewResultListResult
{
    public required IReadOnlyList<AgentReviewResultDto> Results { get; init; }
}
