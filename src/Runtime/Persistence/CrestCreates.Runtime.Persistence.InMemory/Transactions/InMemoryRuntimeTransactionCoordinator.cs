using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.InMemory.Kernel;
using CrestCreates.HumanTask.Abstractions;

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

    internal bool HasAmbientTransaction => _accessor.Current is not null;

    internal InMemoryRuntimePersistenceState RequireAmbientState()
        => _accessor.Current?.StagedState
            ?? throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                "This Runtime operation requires an ambient transaction.");

    internal IDisposable EnterStoreOperation()
    {
        var context = _accessor.Current
            ?? throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                "An InMemory Runtime Store operation requires an ambient Runtime transaction.");
        return context.EnterOperation();
    }

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
            return await work(cancellationToken).ConfigureAwait(false);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var context = new InMemoryRuntimeTransactionContext { StagedState = _committed.Clone() };
        _accessor.Set(context);
        try
        {
            var result = await work(cancellationToken).ConfigureAwait(false);
            ValidateCommittedInvariants(context.StagedState);
            _committed.Workflows.Clear();
            _committed.HumanTasks.Clear();
            _committed.Snapshots.Clear();
            _committed.Receipts.Clear();
            _committed.AbortReceipts.Clear();
            _committed.Outbox.Clear();
            _committed.ContinuationAcceptances.Clear();
            foreach (var (key, value) in context.StagedState.Workflows) _committed.Workflows[key] = value.Snapshot();
            foreach (var (key, value) in context.StagedState.HumanTasks) _committed.HumanTasks[key] = value.Snapshot();
            foreach (var (key, value) in context.StagedState.Snapshots) _committed.Snapshots[key] = (value.Snapshot.Snapshot(), value.Fingerprint);
            foreach (var (key, value) in context.StagedState.Receipts) _committed.Receipts[key] = value;
            foreach (var (key, value) in context.StagedState.AbortReceipts) _committed.AbortReceipts[key] = value;
            foreach (var (key, value) in context.StagedState.Outbox) _committed.Outbox[key] = value.Clone();
            foreach (var (key, value) in context.StagedState.ContinuationAcceptances) _committed.ContinuationAcceptances[key] = value;
            return result;
        }
        finally
        {
            _accessor.Set(null);
            _gate.Release();
        }
    }

    private static void ValidateCommittedInvariants(InMemoryRuntimePersistenceState state)
    {
        foreach (var workflow in state.Workflows.Values)
        {
            if (workflow.WaitingHumanTaskKey is { } waiting
                && (!state.HumanTasks.TryGetValue(waiting, out var task) || task.WorkflowKey != workflow.Key))
            {
                throw new RuntimePersistenceContractException(
                    RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                    "Workflow waiting HumanTask correlation must be reciprocal and tenant-local.");
            }
        }

        foreach (var receipt in state.Receipts.Values)
        {
            if (!state.HumanTasks.TryGetValue(receipt.HumanTaskKey, out var task)
                || task.WorkflowKey != receipt.WorkflowKey)
            {
                throw new RuntimePersistenceContractException(
                    RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                    "Receipt HumanTask correlation must be reciprocal and tenant-local.");
            }
        }

        foreach (var receipt in state.AbortReceipts.Values)
        {
            if (!state.HumanTasks.TryGetValue(receipt.HumanTaskKey, out var task)
                || task.WorkflowKey != receipt.WorkflowKey)
            {
                throw new RuntimePersistenceContractException(
                    RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                    "Abort receipt HumanTask correlation must be reciprocal and tenant-local.");
            }
        }

        foreach (var task in state.HumanTasks.Values)
        {
            var lifecycleIsValid = task.Status switch
            {
                HumanTaskInstanceStatus.Created or HumanTaskInstanceStatus.Assigned
                    => task.CompletedAt is null && task.CancelledAt is null,
                HumanTaskInstanceStatus.Completed or HumanTaskInstanceStatus.CompletionDispatchFailed
                    => task.CompletedAt is not null && task.CancelledAt is null,
                HumanTaskInstanceStatus.Cancelled
                    => task.CompletedAt is null && task.CancelledAt is not null,
                _ => false
            };
            if (!lifecycleIsValid)
            {
                throw new RuntimePersistenceContractException(
                    RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                    "HumanTask status and terminal timestamps must describe one valid lifecycle state.");
            }
        }
    }
}
