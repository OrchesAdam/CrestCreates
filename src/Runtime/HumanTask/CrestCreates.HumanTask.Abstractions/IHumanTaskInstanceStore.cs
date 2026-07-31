namespace CrestCreates.HumanTask.Abstractions;

public interface IHumanTaskInstanceStore
{
    Task AddAsync(HumanTaskInstance instance, CancellationToken cancellationToken = default);
    Task UpdateAsync(HumanTaskInstance instance, long expectedRevision, CancellationToken cancellationToken = default);
    Task<HumanTaskInstance?> GetAsync(
        CrestCreates.Runtime.Persistence.Abstractions.Keys.RuntimeInstanceKey key,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        CrestCreates.Runtime.Persistence.Abstractions.Keys.RuntimeTenantScope scope,
        string assigneeUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(
        CrestCreates.Runtime.Persistence.Abstractions.Keys.RuntimeInstanceKey workflowKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(
        CrestCreates.Runtime.Persistence.Abstractions.Keys.RuntimeTenantScope scope,
        string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(
        CrestCreates.Runtime.Persistence.Abstractions.Keys.RuntimeTenantScope scope,
        string roleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(
        CrestCreates.Runtime.Persistence.Abstractions.Keys.RuntimeTenantScope scope,
        string organizationUnitId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(
        CrestCreates.Runtime.Persistence.Abstractions.Keys.RuntimeTenantScope scope,
        string positionId, CancellationToken cancellationToken = default);
}
