using System.Collections.Concurrent;
using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Stores;

public sealed class InMemoryAgentCompressedContextStore : IAgentCompressedContextStore
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<(string TenantId, string ContextId), AgentCompressedContext> _contexts = new();

    public ValueTask SaveCompressedContextAsync(AgentCompressedContext context, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _contexts[(context.TenantId, context.ContextId)] = context.Snapshot();
        return ValueTask.CompletedTask;
    }

    public ValueTask CreateCompressedContextAsync(AgentCompressedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_gate)
        {
            if (!_contexts.TryAdd((context.TenantId, context.ContextId), context.Snapshot()))
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Context identity already exists.");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentCompressedContext?> GetCompressedContextAsync(string tenantId, string contextId, CancellationToken cancellationToken = default)
    {
        AgentCompressedContext? context;
        lock (_gate)
            _contexts.TryGetValue((tenantId, contextId), out context);
        if (context is null) return new ValueTask<AgentCompressedContext?>((AgentCompressedContext?)null);

        var snapshot = context.Snapshot();
        return new ValueTask<AgentCompressedContext?>(snapshot);
    }

    public ValueTask<AgentCompressedContextBlock?> GetCompressedContextBlockAsync(
        string tenantId,
        string blockId,
        CancellationToken cancellationToken = default)
    {
        AgentCompressedContext[] contexts;
        lock (_gate)
            contexts = _contexts.Values.ToArray();
        foreach (var context in contexts)
        {
            if (!string.Equals(context.TenantId, tenantId, StringComparison.Ordinal))
                continue;

            var block = context.Blocks.FirstOrDefault(item =>
                string.Equals(item.BlockId, blockId, StringComparison.Ordinal));
            if (block is not null)
                return new ValueTask<AgentCompressedContextBlock?>(block.Snapshot());
        }

        return new ValueTask<AgentCompressedContextBlock?>((AgentCompressedContextBlock?)null);
    }
}
