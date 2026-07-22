using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Context handle issuer. Issues handles for trusted contexts only —
/// caller specifies the context ID, the issuer loads the context from store,
/// computes its effective descriptor closure internally, performs closed-world
/// scope validation, and routes through IAgentMemoryAccessArtifactCoordinator.
/// Never directly accesses IAgentMemoryAccessHandleStore.
/// </summary>
public interface IAgentMemoryContextHandleIssuer
{
    ValueTask<AgentMemoryContextHandleIssueResult> IssueForCallerAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        string trustedContextId,
        CancellationToken cancellationToken = default);
}

public sealed record AgentMemoryContextHandleIssueResult
{
    public required string HandleId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public AgentMemoryArtifactCompensationToken? CompensationToken { get; init; }
}
