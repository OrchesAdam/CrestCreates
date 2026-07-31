using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;

namespace CrestCreates.Runtime.Persistence.InMemory.Stores;

internal sealed class InMemoryHumanTaskInstanceStore : IHumanTaskInstanceStore
{
    private readonly InMemoryRuntimeTransactionCoordinator _coordinator;
    public InMemoryHumanTaskInstanceStore(InMemoryRuntimeTransactionCoordinator coordinator) => _coordinator = coordinator;
    public Task AddAsync(HumanTaskInstance instance, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync(_ => { using var guard = _coordinator.EnterStoreOperation(); AddCore(instance); return ValueTask.CompletedTask; }, cancellationToken).AsTask();
    public Task UpdateAsync(HumanTaskInstance instance, long expectedRevision, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync(_ => { using var guard = _coordinator.EnterStoreOperation(); UpdateCore(instance, expectedRevision); return ValueTask.CompletedTask; }, cancellationToken).AsTask();
    public Task<HumanTaskInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync<HumanTaskInstance?>(_ => { using var guard = _coordinator.EnterStoreOperation(); return ValueTask.FromResult(GetCore(key)); }, cancellationToken).AsTask();
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(RuntimeInstanceKey workflowKey, CancellationToken cancellationToken = default) => QueryAsync(i => i.WorkflowKey == workflowKey, cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(RuntimeTenantScope scope, string assigneeUserId, CancellationToken cancellationToken = default) => QueryAsync(i => i.Key.TenantId == scope.TenantId && i.AssigneeUserId == assigneeUserId, cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(RuntimeTenantScope scope, string userId, CancellationToken cancellationToken = default) => QueryAsync(i => i.Key.TenantId == scope.TenantId && i.CandidateUserIds.Contains(userId), cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(RuntimeTenantScope scope, string roleId, CancellationToken cancellationToken = default) => QueryAsync(i => i.Key.TenantId == scope.TenantId && i.CandidateRoleIds.Contains(roleId), cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(RuntimeTenantScope scope, string organizationUnitId, CancellationToken cancellationToken = default) => QueryAsync(i => i.Key.TenantId == scope.TenantId && i.OrganizationUnitId == organizationUnitId, cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(RuntimeTenantScope scope, string positionId, CancellationToken cancellationToken = default) => QueryAsync(i => i.Key.TenantId == scope.TenantId && i.PositionId == positionId, cancellationToken);
    private Task<IReadOnlyList<HumanTaskInstance>> QueryAsync(Func<HumanTaskInstance, bool> predicate, CancellationToken ct) => _coordinator.ExecuteAsync<IReadOnlyList<HumanTaskInstance>>(_ => { using var guard = _coordinator.EnterStoreOperation(); return ValueTask.FromResult<IReadOnlyList<HumanTaskInstance>>(_coordinator.CurrentState.HumanTasks.Values.Where(i => (i.Status is HumanTaskInstanceStatus.Created or HumanTaskInstanceStatus.Assigned) && predicate(i)).OrderBy(i => i.Key.TenantId, StringComparer.Ordinal).ThenBy(i => i.Key.InstanceId, StringComparer.Ordinal).Select(i => i.Snapshot()).ToArray()); }, ct).AsTask();
    private void AddCore(HumanTaskInstance instance)
    {
        Validate(instance);
        if (instance.Revision != 0) throw Contract("New HumanTaskInstance must have Revision 0.");
        EnsureWorkflowExists(instance);
        EnsureNoActiveStepConflict(instance);
        if (!_coordinator.CurrentState.HumanTasks.TryAdd(instance.Key, WithRevision(instance, 1)))
            throw new RuntimeDuplicateEntityException(RuntimeDuplicateEntityCode.DuplicateInstance, "Human task instance already exists.");
    }
    private void UpdateCore(HumanTaskInstance instance, long expectedRevision)
    {
        Validate(instance);
        if (instance.Revision != expectedRevision || !_coordinator.CurrentState.HumanTasks.TryGetValue(instance.Key, out var current) || current.Revision != expectedRevision)
            throw new RuntimeConcurrencyException("Human task revision is stale.");
        EnsureWorkflowExists(instance);
        EnsureNoActiveStepConflict(instance);
        _coordinator.CurrentState.HumanTasks[instance.Key] = WithRevision(instance, expectedRevision + 1);
    }
    private void EnsureNoActiveStepConflict(HumanTaskInstance instance)
    {
        if (instance.WorkflowKey is null || instance.WorkflowStepId is null) return;
        var isActive = instance.CompletedAt is null && instance.CancelledAt is null;
        if (!isActive) return;
        var conflict = _coordinator.CurrentState.HumanTasks.Values.Any(t =>
            t.WorkflowKey == instance.WorkflowKey
            && string.Equals(t.WorkflowStepId, instance.WorkflowStepId, StringComparison.Ordinal)
            && t.CompletedAt is null
            && t.CancelledAt is null
            && !string.Equals(t.Key.InstanceId, instance.Key.InstanceId, StringComparison.Ordinal));
        if (conflict)
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.ActiveStepCorrelationConflict,
                "A HumanTask already exists for the Workflow step correlation.");
    }
    private void EnsureWorkflowExists(HumanTaskInstance instance)
    {
        if (instance.WorkflowKey is null) return;
        if (!_coordinator.CurrentState.Workflows.ContainsKey(instance.WorkflowKey.Value))
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                "HumanTask references a non-existent Workflow instance.");
    }
    private HumanTaskInstance? GetCore(RuntimeInstanceKey key) => _coordinator.CurrentState.HumanTasks.TryGetValue(key, out var value) ? value.Snapshot() : null;
    private static void Validate(HumanTaskInstance i) { ArgumentNullException.ThrowIfNull(i); i.Key.EnsureValid(); i.HumanTaskPin.EnsureValid(); }
    private static HumanTaskInstance WithRevision(HumanTaskInstance value, long revision) { var copy = value.Snapshot(); copy.Revision = revision; copy.UpdatedAt = DateTimeOffset.UtcNow; return copy; }
    private static RuntimePersistenceContractException Contract(string message) => new(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, message);
}
