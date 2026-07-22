using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Projection-neutral source grant. Uses AgentMemoryAccessPrincipal
/// instead of AgentMemoryToolPrincipal.
/// </summary>
public sealed record AgentMemoryAccessSourceGrant
{
    public required string GrantId { get; init; }
    public required AgentContextSourceRef SourceRef { get; init; }
    public required AgentMemoryAccessPrincipal Principal { get; init; }
    public required string ScopeFingerprint { get; init; }
    public IReadOnlyList<DescriptorRef> RequiredDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public bool IsUnscoped { get; init; }
    public required string IssuingOperationId { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public AgentMemorySecurityArtifactState State { get; init; } = AgentMemorySecurityArtifactState.Active;
}
