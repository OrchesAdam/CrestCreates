using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Shared context recall core. Protocol-neutral.
/// </summary>
public interface IAgentContextReadCore
{
    ValueTask<AgentMemoryReadCoreOutcome<RecallAgentContextResult>> RecallContextAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        RecallAgentContextInput input,
        CancellationToken cancellationToken = default);
}
