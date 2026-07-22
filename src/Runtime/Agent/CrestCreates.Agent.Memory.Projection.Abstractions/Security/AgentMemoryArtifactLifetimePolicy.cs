namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Origin-aware artifact lifetime policy. Not a flat 60s for all MCP callers.
/// McpInvocation recall handle/grant → Min(scope lifetime, configurable cap).
/// McpSessionOperation ContextHandle → Min(scope.ResourceHandleLifetime, MCP session cap).
/// AgentToolInvocation/TrustedHostOperation → retain existing scope lifetime.
/// </summary>
public interface IAgentMemoryArtifactLifetimePolicy
{
    TimeSpan GetHandleLifetime(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        string purpose);

    TimeSpan GetGrantLifetime(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        string purpose);
}
