using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// Composite provider that delegates to resource-kind-specific
/// <see cref="IAgentMemoryResourceClosureProvider"/> implementations.
/// </summary>
internal sealed class CompositeCurrentClosureProvider : IAgentMemoryCurrentClosureProvider
{
    private readonly Dictionary<string, IAgentMemoryResourceClosureProvider> _providers;

    public CompositeCurrentClosureProvider(
        IEnumerable<IAgentMemoryResourceClosureProvider> providers)
    {
        _providers = providers.ToDictionary(
            p => p.ResourceKind,
            StringComparer.OrdinalIgnoreCase);
    }

    public ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        AgentMemoryResourceKind resourceKind,
        string tenantId,
        string resourceId,
        CancellationToken ct)
    {
        if (!_providers.TryGetValue(resourceKind.ToString(), out var provider))
            return ValueTask.FromResult<AgentMemoryCurrentClosure?>(null);

        return provider.GetCurrentClosureAsync(tenantId, resourceId, ct);
    }
}
