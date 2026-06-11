namespace CrestCreates.HumanTask.Abstractions;

public interface IHumanTaskInstanceStore
{
    Task SaveAsync(HumanTaskInstance instance, CancellationToken ct = default);

    Task<HumanTaskInstance?> GetByIdAsync(string instanceId, CancellationToken ct = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        string assigneeUserId, CancellationToken ct = default);
}
