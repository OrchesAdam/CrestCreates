using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;

namespace CrestCreates.Runtime.Persistence.InMemory.Stores;

internal sealed class InMemoryHumanTaskInstanceStore : IHumanTaskInstanceStore
{
    private readonly InMemoryRuntimeTransactionCoordinator _coordinator;
    public InMemoryHumanTaskInstanceStore(InMemoryRuntimeTransactionCoordinator coordinator) => _coordinator = coordinator;
    public Task AddAsync(HumanTaskInstance instance, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync(_ => { AddCore(instance); return ValueTask.CompletedTask; }, cancellationToken).AsTask();
    public Task UpdateAsync(HumanTaskInstance instance, long expectedRevision, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync(_ => { UpdateCore(instance, expectedRevision); return ValueTask.CompletedTask; }, cancellationToken).AsTask();
    public Task<HumanTaskInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync<HumanTaskInstance?>(_ => ValueTask.FromResult(GetCore(key)), cancellationToken).AsTask();
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(RuntimeInstanceKey workflowKey, CancellationToken cancellationToken = default) => QueryAsync(i => i.WorkflowKey == workflowKey, cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(RuntimeTenantScope scope, string assigneeUserId, CancellationToken cancellationToken = default) => QueryAsync(i => i.Key.TenantId == scope.TenantId && i.AssigneeUserId == assigneeUserId, cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(RuntimeTenantScope scope, string userId, CancellationToken cancellationToken = default) => QueryAsync(i => i.Key.TenantId == scope.TenantId && i.CandidateUserIds.Contains(userId), cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(RuntimeTenantScope scope, string roleId, CancellationToken cancellationToken = default) => QueryAsync(i => i.Key.TenantId == scope.TenantId && i.CandidateRoleIds.Contains(roleId), cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(RuntimeTenantScope scope, string organizationUnitId, CancellationToken cancellationToken = default) => QueryAsync(i => i.Key.TenantId == scope.TenantId && i.OrganizationUnitId == organizationUnitId, cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(RuntimeTenantScope scope, string positionId, CancellationToken cancellationToken = default) => QueryAsync(i => i.Key.TenantId == scope.TenantId && i.PositionId == positionId, cancellationToken);
    private Task<IReadOnlyList<HumanTaskInstance>> QueryAsync(Func<HumanTaskInstance, bool> predicate, CancellationToken ct) => _coordinator.ExecuteAsync<IReadOnlyList<HumanTaskInstance>>(_ => ValueTask.FromResult<IReadOnlyList<HumanTaskInstance>>(_coordinator.CurrentState.HumanTasks.Values.Where(i => (i.Status is HumanTaskInstanceStatus.Created or HumanTaskInstanceStatus.Assigned) && predicate(i)).OrderBy(i => i.Key.TenantId, StringComparer.Ordinal).ThenBy(i => i.Key.InstanceId, StringComparer.Ordinal).Select(i => i.Snapshot()).ToArray()), ct).AsTask();
    private void AddCore(HumanTaskInstance instance) { Validate(instance); if (instance.Revision != 0) throw Contract("New HumanTaskInstance must have Revision 0."); if (!_coordinator.CurrentState.HumanTasks.TryAdd(instance.Key, WithRevision(instance, 1))) throw new RuntimeDuplicateEntityException(RuntimeDuplicateEntityCode.DuplicateInstance, "Human task instance already exists."); }
    private void UpdateCore(HumanTaskInstance instance, long expectedRevision) { Validate(instance); if (instance.Revision != expectedRevision || !_coordinator.CurrentState.HumanTasks.TryGetValue(instance.Key, out var current) || current.Revision != expectedRevision) throw new RuntimeConcurrencyException("Human task revision is stale."); _coordinator.CurrentState.HumanTasks[instance.Key] = WithRevision(instance, expectedRevision + 1); }
    private HumanTaskInstance? GetCore(RuntimeInstanceKey key) => _coordinator.CurrentState.HumanTasks.TryGetValue(key, out var value) ? value.Snapshot() : null;
    private static void Validate(HumanTaskInstance i) { ArgumentNullException.ThrowIfNull(i); i.Key.EnsureValid(); i.HumanTaskPin.EnsureValid(); }
    private static HumanTaskInstance WithRevision(HumanTaskInstance value, long revision) { var copy = value.Snapshot(); copy.Revision = revision; copy.UpdatedAt = DateTimeOffset.UtcNow; return copy; }
    private static RuntimePersistenceContractException Contract(string message) => new(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, message);
}
