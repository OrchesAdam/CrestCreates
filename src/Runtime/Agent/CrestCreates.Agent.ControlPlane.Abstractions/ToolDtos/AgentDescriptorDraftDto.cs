using CrestCreates.Metadata.Abstractions;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Adapter-safe projection of DescriptorDraft.
/// Replaces DescriptorDraft in all tool results and request DTOs.
/// Payload uses AgentDraftPayloadDto (nested one-of) instead of
/// abstract DescriptorDraftPayload.
/// </summary>
public sealed record AgentDescriptorDraftDto
{
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required DescriptorKind DescriptorKind { get; init; }
    public required string DescriptorId { get; init; }
    public required DraftAbstractions.DescriptorDraftOperation Operation { get; init; }
    public required DraftAbstractions.DescriptorDraftAuthorKind AuthorKind { get; init; }
    public required string AuthorId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required AgentDraftPayloadDto Payload { get; init; }
    public string? BaseVersion { get; init; }
    public string? ProposedVersion { get; init; }
    public string? Intent { get; init; }
    public string? Rationale { get; init; }
    public string? CorrelationId { get; init; }
    public string? Source { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public DraftAbstractions.DescriptorDraftStatus Status { get; init; }
}
