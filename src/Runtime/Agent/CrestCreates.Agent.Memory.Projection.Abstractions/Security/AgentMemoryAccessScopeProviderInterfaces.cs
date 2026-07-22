namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public interface IAgentMemoryAccessScopeProvider
{
    ValueTask<AgentMemoryAccessScope> ResolveAsync(
        AgentMemoryAccessPrincipal principal,
        CancellationToken cancellationToken = default);
}

public interface IAgentMemoryAccessScopeProviderCapabilities
{
    bool Supports(AgentMemoryCallerKind callerKind);
}
