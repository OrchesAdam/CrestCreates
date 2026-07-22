using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Projection-neutral batch key. Uses AgentMemoryArtifactOriginKind
/// instead of AgentMemorySecurityArtifactBatchOriginKind.
/// </summary>
public sealed record AgentMemoryAccessArtifactBatchKey
{
    public required AgentMemoryArtifactOriginKind OriginKind { get; init; }
    public required CanonicalHash OriginBindingHash { get; init; }
    public required string ArtifactPurpose { get; init; }
    public required int PreparationOrdinal { get; init; }
    public required CanonicalHash ArtifactPlanHash { get; init; }

    public string ToCanonicalKey()
        => string.Join("|", OriginKind, Segment(OriginBindingHash),
            Segment(ArtifactPurpose), PreparationOrdinal, Segment(ArtifactPlanHash));

    public string ToIdentityKey()
        => string.Join("|", OriginKind, Segment(OriginBindingHash),
            Segment(ArtifactPurpose), PreparationOrdinal);

    private static string Segment(CanonicalHash value)
        => $"{value.Value.Length}:{value.Value}:{value.AlgorithmVersion}:{value.ArtifactKind}:{value.Scope}:{value.Purpose}:{value.ContractVersion}:{value.CanonicalShapeVersion}";

    private static string Segment(string value)
        => $"{value.Length}:{value}";
}
