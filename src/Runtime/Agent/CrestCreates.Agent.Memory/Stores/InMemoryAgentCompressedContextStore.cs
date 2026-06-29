using System.Collections.Concurrent;
using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Stores;

public sealed class InMemoryAgentCompressedContextStore : IAgentCompressedContextStore
{
    private readonly ConcurrentDictionary<(string TenantId, string ContextId), AgentCompressedContext> _contexts = new();

    public ValueTask SaveCompressedContextAsync(AgentCompressedContext context, CancellationToken cancellationToken = default)
    {
        _contexts[(context.TenantId, context.ContextId)] = context with
        {
            Blocks = context.Blocks
                .Select(b => b with { SourceRefs = b.SourceRefs.ToArray(), Diagnostics = b.Diagnostics.ToArray() })
                .ToArray()
        };
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentCompressedContext?> GetCompressedContextAsync(string tenantId, string contextId, CancellationToken cancellationToken = default)
    {
        _contexts.TryGetValue((tenantId, contextId), out var context);
        if (context is null) return new ValueTask<AgentCompressedContext?>((AgentCompressedContext?)null);

        var snapshot = context with
        {
            Blocks = context.Blocks
                .Select(b => b with { SourceRefs = b.SourceRefs.ToArray(), Diagnostics = b.Diagnostics.ToArray() })
                .ToArray()
        };
        return new ValueTask<AgentCompressedContext?>(snapshot);
    }
}
