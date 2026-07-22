namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Projection-neutral grant resolver. Full Principal record equality required.
/// </summary>
public interface IAgentMemoryAccessGrantResolver
{
    ValueTask<AgentMemoryAccessSourceGrant?> ResolveAsync(
        string grantId,
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        CancellationToken cancellationToken = default);
}
