using System.Collections.Concurrent;

namespace CrestCreates.Agent.Orchestration;

public sealed class InMemoryAgentExecutionStore : IAgentExecutionStore
{
    private readonly ConcurrentDictionary<AgentExecutionKey, AgentExecutionSnapshot> _executions = new();

    public ValueTask<AgentExecutionSnapshot?> FindAsync(AgentExecutionKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_executions.GetValueOrDefault(key));
    }

    public ValueTask<bool> TryCreateAsync(AgentExecutionSnapshot execution, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_executions.TryAdd(execution.Key, execution));
    }

    public ValueTask<bool> TryReplaceAsync(
        AgentExecutionSnapshot execution, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            _executions.TryGetValue(execution.Key, out var current) &&
            current.Revision == expectedRevision &&
            _executions.TryUpdate(execution.Key, execution, current));
    }
}
