using CrestCreates.Agent.Memory.Projection.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// Origin-aware artifact lifetime policy. McpInvocation recall handle/grant
/// → Min(scope lifetime, configurable cap). McpSessionOperation ContextHandle
/// → Min(scope.ResourceHandleLifetime, MCP session cap).
/// </summary>
internal sealed class DefaultAgentMemoryArtifactLifetimePolicy : IAgentMemoryArtifactLifetimePolicy
{
    private readonly AgentMemoryProjectionSecurityOptions _options;

    public DefaultAgentMemoryArtifactLifetimePolicy(AgentMemoryProjectionSecurityOptions options)
    {
        _options = options;
    }

    public TimeSpan GetHandleLifetime(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        string purpose)
    {
        return origin.Kind switch
        {
            AgentMemoryArtifactOriginKind.McpInvocation =>
                Min(scope.ResourceHandleLifetime, _options.McpInvocationProvisionalLifetime),
            AgentMemoryArtifactOriginKind.McpSessionOperation =>
                Min(scope.ResourceHandleLifetime, _options.McpSessionLifetimeCap),
            AgentMemoryArtifactOriginKind.AgentToolInvocation =>
                scope.ResourceHandleLifetime,
            AgentMemoryArtifactOriginKind.TrustedHostOperation =>
                scope.ResourceHandleLifetime,
            _ => throw new InvalidOperationException(
                $"Unsupported origin kind: {origin.Kind}")
        };
    }

    public TimeSpan GetGrantLifetime(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        string purpose)
    {
        return origin.Kind switch
        {
            AgentMemoryArtifactOriginKind.McpInvocation =>
                Min(scope.ExpansionGrantLifetime, _options.McpInvocationProvisionalLifetime),
            AgentMemoryArtifactOriginKind.McpSessionOperation =>
                scope.ExpansionGrantLifetime,
            AgentMemoryArtifactOriginKind.AgentToolInvocation =>
                scope.ExpansionGrantLifetime,
            AgentMemoryArtifactOriginKind.TrustedHostOperation =>
                scope.ExpansionGrantLifetime,
            _ => throw new InvalidOperationException(
                $"Unsupported origin kind: {origin.Kind}")
        };
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
