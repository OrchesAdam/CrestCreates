namespace CrestCreates.Agent.DraftContracts.Dto;

/// <summary>
/// Represents a versioned descriptor reference where <see cref="Metadata.Abstractions.DescriptorRef"/>
/// cannot losslessly represent the domain shape (e.g., when SelectionMode or ExpectedContractHash are needed).
/// </summary>
public sealed record AgentVersionedDescriptorRefDto
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public int SelectionMode { get; init; }
    public string? ExpectedContractHash { get; init; }
    public string? Namespace { get; init; }
}
