using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Projection-neutral prepared artifact snapshot for batch operations.
/// Replaces AgentMemoryPreparedSecurityArtifact for new projection interfaces.
/// PlanHash enables retry idempotency by binding the full logical resource graph
/// without random artifact IDs.
/// </summary>
public sealed record AgentMemoryAccessPreparedArtifact
{
    public required AgentMemorySecurityArtifactKind Kind { get; init; }
    public required string ResourceKind { get; init; }
    public required string ResourceId { get; init; }
    public required string ArtifactId { get; init; }
    public required PreparedArtifactDisposition Disposition { get; init; }
    public required CanonicalHash PlanHash { get; init; }
}
