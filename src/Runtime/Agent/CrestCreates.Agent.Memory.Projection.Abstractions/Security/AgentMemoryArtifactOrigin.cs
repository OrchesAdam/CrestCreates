using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Per-invocation origin tracking. BindingHash is unique per operation identity.
/// </summary>
public sealed record AgentMemoryArtifactOrigin
{
    public required AgentMemoryArtifactOriginKind Kind { get; init; }
    public required CanonicalHash BindingHash { get; init; }
    public required string OperationId { get; init; }
}
