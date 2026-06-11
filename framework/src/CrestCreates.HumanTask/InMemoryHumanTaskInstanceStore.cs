using System.Collections.Concurrent;
using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class InMemoryHumanTaskInstanceStore : IHumanTaskInstanceStore
{
    private readonly ConcurrentDictionary<string, HumanTaskInstance> _instances = new();

    public Task SaveAsync(HumanTaskInstance instance, CancellationToken ct = default)
    {
        _instances[instance.Id] = instance;
        return Task.CompletedTask;
    }

    public Task<HumanTaskInstance?> GetByIdAsync(string instanceId, CancellationToken ct = default)
    {
        _instances.TryGetValue(instanceId, out var instance);
        return Task.FromResult(instance);
    }

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        string assigneeUserId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.AssigneeUserId == assigneeUserId)
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }
}
