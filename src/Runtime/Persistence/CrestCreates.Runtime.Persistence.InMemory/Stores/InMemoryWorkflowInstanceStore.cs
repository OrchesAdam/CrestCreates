using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Runtime.Persistence.InMemory.Stores;

public sealed class InMemoryWorkflowInstanceStore : IWorkflowInstanceStore
{
    private readonly InMemoryRuntimeTransactionCoordinator _coordinator;
    internal InMemoryWorkflowInstanceStore(InMemoryRuntimeTransactionCoordinator coordinator) => _coordinator = coordinator;

    public Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(_ => { using var guard = _coordinator.EnterStoreOperation(); AddCore(instance); return ValueTask.CompletedTask; }, cancellationToken).AsTask();

    public Task UpdateAsync(WorkflowInstance instance, long expectedRevision, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(_ => { using var guard = _coordinator.EnterStoreOperation(); UpdateCore(instance, expectedRevision); return ValueTask.CompletedTask; }, cancellationToken).AsTask();

    public Task<WorkflowInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync<WorkflowInstance?>(_ => { using var guard = _coordinator.EnterStoreOperation(); return ValueTask.FromResult(GetCore(key)); }, cancellationToken).AsTask();

    public Task<WorkflowInstance?> GetByWaitingHumanTaskAsync(RuntimeInstanceKey humanTaskKey, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync<WorkflowInstance?>(_ => { using var guard = _coordinator.EnterStoreOperation(); return ValueTask.FromResult(_coordinator.CurrentState.Workflows.Values
            .Where(w => w.Status == WorkflowInstanceStatus.Suspended && w.WaitingHumanTaskKey == humanTaskKey)
            .OrderBy(w => w.Key.TenantId, StringComparer.Ordinal).ThenBy(w => w.Key.InstanceId, StringComparer.Ordinal)
            .Select(w => w.Snapshot()).SingleOrDefault()); }, cancellationToken).AsTask();

    private void AddCore(WorkflowInstance instance)
    {
        Validate(instance);
        if (instance.Revision != 0) throw Contract("New WorkflowInstance must have Revision 0.");
        if (!_coordinator.CurrentState.Workflows.TryAdd(instance.Key, WithRevision(instance, 1)))
            throw new RuntimeDuplicateEntityException(RuntimeDuplicateEntityCode.DuplicateInstance, "Workflow instance already exists.");
    }
    private void UpdateCore(WorkflowInstance instance, long expectedRevision)
    {
        Validate(instance);
        if (instance.Revision != expectedRevision || !_coordinator.CurrentState.Workflows.TryGetValue(instance.Key, out var current)
            || current.Revision != expectedRevision) throw new RuntimeConcurrencyException("Workflow revision is stale.");
        EnsureWaitingTaskIsReciprocal(instance);
        _coordinator.CurrentState.Workflows[instance.Key] = WithRevision(instance, expectedRevision + 1);
    }
    private WorkflowInstance? GetCore(RuntimeInstanceKey key)
        => _coordinator.CurrentState.Workflows.TryGetValue(key, out var value) ? value.Snapshot() : null;
    private void EnsureWaitingTaskIsReciprocal(WorkflowInstance instance)
    {
        if (instance.WaitingHumanTaskKey is not { } waiting)
            return;
        if (!_coordinator.CurrentState.HumanTasks.TryGetValue(waiting, out var task)
            || task.WorkflowKey != instance.Key)
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                "Workflow waiting HumanTask correlation must be reciprocal and tenant-local.");
        }
    }
    private static void Validate(WorkflowInstance i) { ArgumentNullException.ThrowIfNull(i); i.Key.EnsureValid(); i.WorkflowPin.EnsureValid(); }
    private static WorkflowInstance WithRevision(WorkflowInstance value, long revision) { var copy = value.Snapshot(); copy.Revision = revision; copy.UpdatedAt = DateTimeOffset.UtcNow; return copy; }
    private static RuntimePersistenceContractException Contract(string message) => new(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, message);
}
