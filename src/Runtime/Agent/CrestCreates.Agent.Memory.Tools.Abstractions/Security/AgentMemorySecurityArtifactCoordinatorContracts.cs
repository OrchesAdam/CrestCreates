using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Tools;

namespace CrestCreates.Agent.Memory.Tools;

public sealed record AgentMemoryPreparedSecurityArtifacts
{
    public required AgentMemoryResourceHandleIssueResult? Handles { get; init; }
    public required AgentMemoryGrantIssueResult? Grants { get; init; }
}

/// <summary>
/// The single preparation boundary for plan hashing, dual-origin binding, and
/// handle/grant issuance. Callers never construct store batch identities.
/// </summary>
public interface IAgentMemorySecurityArtifactCoordinator
{
    ValueTask<AgentMemoryPreparedSecurityArtifacts> PrepareForAgentToolAsync(
        AgentToolInvocationBindingSnapshot binding,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        string artifactPurpose,
        int preparationOrdinal,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        CancellationToken cancellationToken = default);

    ValueTask RevokeCreatedAsync(
        AgentMemoryPreparedSecurityArtifacts prepared,
        CancellationToken cancellationToken = default);

    ValueTask<AgentMemoryPreparedSecurityArtifacts> PrepareForHostAsync(
        AgentMemoryHostArtifactBatchKey hostBatchKey,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        AgentMemoryHistorySourceKind sourceKind,
        string sourceId,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        CancellationToken cancellationToken = default);
}
