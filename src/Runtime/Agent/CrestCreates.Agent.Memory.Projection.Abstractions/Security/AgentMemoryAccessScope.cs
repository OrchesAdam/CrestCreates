using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Projection-neutral access scope. Preserves all fields from AgentMemoryToolAccessScope
/// plus adds MaxContextRecallCharacters for MCP ctx_recall budget.
/// </summary>
public sealed record AgentMemoryAccessScope
{
    public required string TenantId { get; init; }
    public required IReadOnlyList<DescriptorRef> VisibleDescriptorRefs { get; init; }
    public required bool AllowUnscopedMemory { get; init; }

    public required int MaxVisibleDescriptorRefs { get; init; }
    public required int MaxRecallCount { get; init; }
    public required int MaxRecallCharacters { get; init; }
    public required int MaxExpansionCharacters { get; init; }

    public required int MaxContextRecallCharacters { get; init; }

    public required int MaxCompressedBlockCount { get; init; }
    public required int MaxCompressedBlockCharacters { get; init; }
    public required int MaxCandidateCount { get; init; }
    public required int MaxCandidateCharacters { get; init; }
    public required int MaxSourceRefsPerArtifact { get; init; }
    public required int MaxGrantsPerResource { get; init; }
    public required int MaxGrantsPerOperation { get; init; }
    public required int MaxResourceHandlesPerOperation { get; init; }
    public required int MaxActiveResourceHandlesPerResource { get; init; }
    public required int MaxAuditFacts { get; init; }
    public required int MaxTagsPerResource { get; init; }
    public required TimeSpan ExpansionGrantLifetime { get; init; }
    public required TimeSpan ResourceHandleLifetime { get; init; }
}
