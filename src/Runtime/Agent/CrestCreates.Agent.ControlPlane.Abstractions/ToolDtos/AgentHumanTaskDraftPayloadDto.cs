using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record AgentHumanTaskDraftPayloadDto
{
    public DescriptorRef? DescriptorRef { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? State { get; init; }
    public string? AssignmentStrategy { get; init; }
    public string? ContractHash { get; init; }
    public string? DefinitionHash { get; init; }
    public int? Version { get; init; }
    public DescriptorRef? InputSchema { get; init; }
    public DescriptorRef? OutputSchema { get; init; }
    public DescriptorRef? Interaction { get; init; }
    public string? Timeout { get; init; }
}
