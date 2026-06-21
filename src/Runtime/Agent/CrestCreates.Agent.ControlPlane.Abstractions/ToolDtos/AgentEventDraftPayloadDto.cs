using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record AgentEventDraftPayloadDto
{
    public DescriptorRef? DescriptorRef { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? State { get; init; }
    public string? EventKind { get; init; }
    public string? EventType { get; init; }
    public string? ContractHash { get; init; }
    public string? DefinitionHash { get; init; }
    public int? Version { get; init; }
    public DescriptorRef? PayloadSchema { get; init; }
    public string? Importance { get; init; }
    public string? ChangeKind { get; init; }
}
