namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorDraftListResult
{
    public required IReadOnlyList<AgentDescriptorDraftDto> Drafts { get; init; }
    public required int TotalCount { get; init; }
}
