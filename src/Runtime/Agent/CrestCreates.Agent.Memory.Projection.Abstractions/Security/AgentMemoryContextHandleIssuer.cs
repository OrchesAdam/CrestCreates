using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Context handle issuer. Routes through IAgentMemoryAccessArtifactCoordinator.PrepareAsync —
/// never directly accesses IAgentMemoryAccessHandleStore.
/// Internally resolves scope via IAgentMemoryAccessScopeProvider.ResolveAsync(principal).
/// Returns only opaque HandleId + ExpiresAt.
/// </summary>
public interface IAgentMemoryContextHandleIssuer
{
    ValueTask<AgentMemoryContextHandleIssueResult> IssueAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        string purpose,
        AgentMemoryResourceKind resourceKind,
        string resourceId,
        CancellationToken cancellationToken = default);
}

public sealed record AgentMemoryContextHandleIssueResult
{
    public required string HandleId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
