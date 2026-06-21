using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record AgentCapabilityDraftPayloadDto
{
    public DescriptorRef? DescriptorRef { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? State { get; init; }
    public DescriptorRef? InputSchema { get; init; }
    public DescriptorRef? OutputSchema { get; init; }
    public string? CapabilityKind { get; init; }
    public string[]? Categories { get; init; }
    public DescriptorRef[]? Produces { get; init; }
    public DescriptorRef[]? Consumes { get; init; }
    public string[]? SemanticTags { get; init; }
    public string[]? Permissions { get; init; }
    public string? RiskLevel { get; init; }
    public string? ContractHash { get; init; }
    public string? DefinitionHash { get; init; }
    public int? Version { get; init; }
}
