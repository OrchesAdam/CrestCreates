namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Domain-specific provider that resolves the current descriptor closure
/// for a single resource kind. Registered by domain modules (e.g. Agent.Memory.Tools).
/// </summary>
public interface IAgentMemoryResourceClosureProvider
{
    string ResourceKind { get; }

    ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string resourceId,
        CancellationToken cancellationToken = default);
}
