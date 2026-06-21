using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record AgentWorkflowDraftPayloadDto
{
    public DescriptorRef? DescriptorRef { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? State { get; init; }
    public string? ContractHash { get; init; }
    public string? DefinitionHash { get; init; }
    public int? Version { get; init; }
    public DescriptorRef? VariableSchema { get; init; }
}
