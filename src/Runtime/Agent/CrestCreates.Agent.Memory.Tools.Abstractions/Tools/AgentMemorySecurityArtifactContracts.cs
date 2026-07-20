using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Tools;

public enum AgentMemorySecurityArtifactBatchOriginKind
{
    Unknown = 0,
    AgentToolInvocation = 1,
    TrustedHostOperation = 2
}

public sealed record AgentMemorySecurityArtifactBatchKey
{
    public required AgentMemorySecurityArtifactBatchOriginKind OriginKind { get; init; }
    public required CanonicalHash OriginBindingHash { get; init; }
    public required string ArtifactPurpose { get; init; }
    public required int PreparationOrdinal { get; init; }
    public required CanonicalHash ArtifactPlanHash { get; init; }

    /// <summary>Returns the idempotency key including the concrete artifact plan.</summary>
    public string ToCanonicalKey()
        => string.Join("|", OriginKind, Segment(OriginBindingHash),
            Segment(ArtifactPurpose), PreparationOrdinal, Segment(ArtifactPlanHash));

    /// <summary>Returns the retry identity that deliberately excludes the plan hash.</summary>
    public string ToIdentityKey()
        => string.Join("|", OriginKind, Segment(OriginBindingHash),
            Segment(ArtifactPurpose), PreparationOrdinal);

    private static string Segment(CanonicalHash value)
        => $"{value.Value.Length}:{value.Value}:{value.AlgorithmVersion}:{value.ArtifactKind}:{value.Scope}:{value.Purpose}:{value.ContractVersion}:{value.CanonicalShapeVersion}";

    private static string Segment(string value)
        => $"{value.Length}:{value}";
}

public sealed record AgentMemoryHostArtifactBatchKey
{
    public required string HostOperationId { get; init; }
    public required CanonicalHash OperationFingerprint { get; init; }
    public required string ArtifactPurpose { get; init; }
}

public enum AgentMemorySecurityArtifactKind
{
    Unknown = 0,
    ResourceHandle = 1,
    SourceGrant = 2
}

public enum PreparedArtifactDisposition
{
    Unknown = 0,
    CreatedByBatch = 1,
    ReusedExisting = 2
}

/// <summary>
/// Immutable prepared-artifact snapshot. Batch rollback matches ArtifactId
/// explicitly so it remains correct even if a provider changes this type from
/// a record implementation in a future compatibility revision.
/// </summary>
public sealed record AgentMemoryPreparedSecurityArtifact
{
    public required AgentMemorySecurityArtifactKind Kind { get; init; }
    public required string ResourceKind { get; init; }
    public required string ResourceId { get; init; }
    public required string ArtifactId { get; init; }
    public required PreparedArtifactDisposition Disposition { get; init; }
}

public interface IAgentMemorySecurityArtifactBatchStore
{
    ValueTask<IReadOnlyList<AgentMemoryPreparedSecurityArtifact>> PrepareAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryPreparedSecurityArtifact> plan,
        CancellationToken cancellationToken = default);

    ValueTask RevokeCreatedAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryPreparedSecurityArtifact> artifacts,
        CancellationToken cancellationToken = default);
}
