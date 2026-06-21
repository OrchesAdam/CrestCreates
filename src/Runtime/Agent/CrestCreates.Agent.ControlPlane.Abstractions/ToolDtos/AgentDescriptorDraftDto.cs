using CrestCreates.Metadata.Abstractions;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Adapter-safe projection of DescriptorDraft.
/// Replaces DescriptorDraft in all tool results and request DTOs.
/// Payload uses generated AgentDraftPayloadDto (source-generated from
/// CrestCreates.Agent.DraftContracts specs) instead of abstract DescriptorDraftPayload.
///
/// Ownership note (#42): The draft payload contract (AgentDraftPayloadDto,
/// AgentDraftPayloadPatchDto, projection/merge logic) is fully source-generated
/// by AgentDraftContractGenerator. This DTO wraps the generated payload but is
/// itself a ControlPlane Tool DTO — not generated. Future work may migrate this
/// wrapper into DraftContracts.Dto as well, but for 7c.v1 it remains here.
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
