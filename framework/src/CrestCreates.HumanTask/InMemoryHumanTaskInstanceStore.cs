using System.Collections.Concurrent;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class InMemoryHumanTaskInstanceStore : IHumanTaskInstanceStore
{
    private readonly ConcurrentDictionary<string, HumanTaskInstance> _instances = new();

    public Task SaveAsync(HumanTaskInstance instance, CancellationToken ct = default)
    {
        var snapshot = instance.Clone();
        snapshot.UpdatedAt = DateTimeOffset.UtcNow;

        while (true)
        {
            if (!_instances.TryGetValue(instance.Id, out var existing))
            {
                // First save — insert with fresh stamp
                snapshot.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                if (_instances.TryAdd(instance.Id, snapshot))
                {
                    instance.ConcurrencyStamp = snapshot.ConcurrencyStamp;
                    instance.UpdatedAt = snapshot.UpdatedAt;
                    return Task.CompletedTask;
                }
                // Race: another thread inserted — retry
                continue;
            }

            // Update existing — check concurrency stamp atomically
            if (existing.ConcurrencyStamp != instance.ConcurrencyStamp)
                throw new RuntimeConcurrencyException(
                    $"Concurrency conflict for HumanTaskInstance '{instance.Id}'. " +
                    $"Expected stamp '{instance.ConcurrencyStamp}', actual '{existing.ConcurrencyStamp}'.");

            snapshot.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            if (_instances.TryUpdate(instance.Id, snapshot, existing))
            {
                instance.ConcurrencyStamp = snapshot.ConcurrencyStamp;
                instance.UpdatedAt = snapshot.UpdatedAt;
                return Task.CompletedTask;
            }
            // Race: another thread updated — retry
        }
    }

    public Task<HumanTaskInstance?> GetByIdAsync(string instanceId, CancellationToken ct = default)
    {
        if (_instances.TryGetValue(instanceId, out var existing))
            return Task.FromResult<HumanTaskInstance?>(existing.Clone());
        return Task.FromResult<HumanTaskInstance?>(null);
    }

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        string assigneeUserId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.AssigneeUserId == assigneeUserId)
            .Select(i => i.Clone())
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(
        string workflowInstanceId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.WorkflowInstanceId == workflowInstanceId)
            .Select(i => i.Clone())
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(
        string userId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.CandidateUserIds.Contains(userId))
            .Select(i => i.Clone())
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(
        string roleId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.CandidateRoleIds.Contains(roleId))
            .Select(i => i.Clone())
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(
        string organizationUnitId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.OrganizationUnitId == organizationUnitId)
            .Select(i => i.Clone())
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(
        string positionId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.PositionId == positionId)
            .Select(i => i.Clone())
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }
}
