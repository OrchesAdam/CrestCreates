using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.InMemory.Kernel;

namespace CrestCreates.Runtime.Persistence.InMemory.Transactions;

internal sealed class InMemoryRuntimeTransactionCoordinator : IRuntimeTransactionCoordinator
{
    private readonly InMemoryRuntimePersistenceState _committed = new();
    private readonly InMemoryRuntimeTransactionAccessor _accessor;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public InMemoryRuntimeTransactionCoordinator(InMemoryRuntimeTransactionAccessor accessor)
        => _accessor = accessor;

    internal InMemoryRuntimePersistenceState CurrentState
        => _accessor.Current?.StagedState ?? _committed;

    public async ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> work, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync<object?>(async ct => { await work(ct); return null; }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var outer = _accessor.Current;
        if (outer is not null)
        {
            // Nested calls intentionally join the outer staged state. Store
            // operations remain serialized by the caller's transaction flow.
            return await work(cancellationToken).ConfigureAwait(false);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var context = new InMemoryRuntimeTransactionContext { StagedState = _committed.Clone() };
        _accessor.Set(context);
        try
        {
            var result = await work(cancellationToken).ConfigureAwait(false);
            _committed.Workflows.Clear();
            _committed.HumanTasks.Clear();
            foreach (var (key, value) in context.StagedState.Workflows) _committed.Workflows[key] = value.Snapshot();
            foreach (var (key, value) in context.StagedState.HumanTasks) _committed.HumanTasks[key] = value.Snapshot();
            return result;
        }
        finally
        {
            _accessor.Set(null);
            _gate.Release();
        }
    }
}
