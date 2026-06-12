namespace CrestCreates.HumanTask.Abstractions;

public interface IHumanTaskInstanceStore
{
    Task SaveAsync(HumanTaskInstance instance, CancellationToken ct = default);

    Task<HumanTaskInstance?> GetByIdAsync(string instanceId, CancellationToken ct = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        string assigneeUserId, CancellationToken ct = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(
        string workflowInstanceId, CancellationToken ct = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(
        string userId, CancellationToken ct = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(
        string roleId, CancellationToken ct = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(
        string organizationUnitId, CancellationToken ct = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(
        string positionId, CancellationToken ct = default);
}
