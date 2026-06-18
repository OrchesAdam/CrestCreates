using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record CreateDescriptorDraftRequest
{
    public required DescriptorKind DescriptorKind { get; init; }
    public required string DescriptorId { get; init; }
    public required DraftAbstractions.DescriptorDraftOperation Operation { get; init; }
    public required DraftAbstractions.DescriptorDraftPayload Payload { get; init; }
    public string? BaseVersion { get; init; }
    public string? ProposedVersion { get; init; }
    public string? Intent { get; init; }
    public string? Rationale { get; init; }
    public string? CorrelationId { get; init; }
    public string? Source { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
