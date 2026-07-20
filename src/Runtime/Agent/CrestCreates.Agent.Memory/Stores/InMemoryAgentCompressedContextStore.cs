using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Stores;

public sealed class InMemoryAgentCompressedContextStore : IAgentCompressedContextStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string TenantId, string ContextId), AgentCompressedContext> _contexts = new();
    private readonly Dictionary<(string TenantId, string BlockId), AgentCompressedContextBlock> _blocks = new();

    public ValueTask SaveCompressedContextAsync(AgentCompressedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_gate)
        {
            var key = (context.TenantId, context.ContextId);
            var snapshot = ValidateAndSnapshot(context);
            EnsureBlockIdentitiesAvailable(snapshot, key);
            if (_contexts.TryGetValue(key, out var existing))
            {
                foreach (var block in existing.Blocks)
                    _blocks.Remove((context.TenantId, block.BlockId));
            }
            _contexts[key] = snapshot;
            foreach (var block in snapshot.Blocks)
                _blocks[(context.TenantId, block.BlockId)] = block;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask CreateCompressedContextAsync(AgentCompressedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_gate)
        {
            var key = (context.TenantId, context.ContextId);
            if (_contexts.ContainsKey(key))
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Context identity already exists.");
            var snapshot = ValidateAndSnapshot(context);
            EnsureBlockIdentitiesAvailable(snapshot, key);
            _contexts[key] = snapshot;
            foreach (var block in snapshot.Blocks)
                _blocks[(context.TenantId, block.BlockId)] = block;
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
        lock (_gate)
            return ValueTask.FromResult(_blocks.TryGetValue((tenantId, blockId), out var block)
                ? (AgentCompressedContextBlock?)block.Snapshot()
                : null);
    }

    private static AgentCompressedContext ValidateAndSnapshot(AgentCompressedContext context)
    {
        var duplicate = context.Blocks
            .GroupBy(item => item.BlockId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Compressed context contains duplicate BlockId values.");
        if (context.Blocks.Any(item => !string.Equals(item.TenantId, context.TenantId, StringComparison.Ordinal)))
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.TenantMismatch, "Compressed context block tenant does not match the context tenant.");
        return context.Snapshot();
    }

    private void EnsureBlockIdentitiesAvailable(AgentCompressedContext context, (string TenantId, string ContextId) contextKey)
    {
        if (context.Blocks.Any(item => _blocks.ContainsKey((context.TenantId, item.BlockId))
            && (!_contexts.TryGetValue(contextKey, out var existing)
                || existing.Blocks.All(block => !string.Equals(block.BlockId, item.BlockId, StringComparison.Ordinal)))))
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Compressed context BlockId already exists.");
    }
}
