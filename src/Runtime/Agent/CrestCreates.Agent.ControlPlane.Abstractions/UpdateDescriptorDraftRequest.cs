namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record UpdateDescriptorDraftRequest
{
    public required string DraftId { get; init; }
    public AgentDraftPayloadPatchDto? Payload { get; init; }
    public string? ProposedVersion { get; init; }
    public string? Intent { get; init; }
    public string? Rationale { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
