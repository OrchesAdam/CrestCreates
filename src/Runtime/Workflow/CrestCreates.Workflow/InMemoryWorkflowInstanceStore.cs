using System.Collections.Concurrent;
using System.Linq;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class InMemoryWorkflowInstanceStore : IWorkflowInstanceStore
{
    private readonly ConcurrentDictionary<string, WorkflowInstance> _instances = new();

    public Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
        var snapshot = instance.Snapshot();
        snapshot.UpdatedAt = DateTimeOffset.UtcNow;

        while (true)
        {
            if (!_instances.TryGetValue(instance.InstanceId, out var existing))
            {
                // First save — insert with fresh stamp
                snapshot.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                if (_instances.TryAdd(instance.InstanceId, snapshot))
                {
                    instance.ConcurrencyStamp = snapshot.ConcurrencyStamp;
                    instance.UpdatedAt = snapshot.UpdatedAt;
                    return Task.CompletedTask;
                }
                // Race: another thread inserted between TryGetValue and TryAdd — retry
                continue;
            }

            // Update existing — check concurrency stamp atomically
            if (existing.ConcurrencyStamp != instance.ConcurrencyStamp)
                throw new RuntimeConcurrencyException(
                    $"Concurrency conflict for WorkflowInstance '{instance.InstanceId}'. " +
                    $"Expected stamp '{instance.ConcurrencyStamp}', actual '{existing.ConcurrencyStamp}'.");

            snapshot.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            if (_instances.TryUpdate(instance.InstanceId, snapshot, existing))
            {
                instance.ConcurrencyStamp = snapshot.ConcurrencyStamp;
                instance.UpdatedAt = snapshot.UpdatedAt;
                return Task.CompletedTask;
            }
            // Race: another thread updated — retry
        }
    }

    public Task<WorkflowInstance?> GetAsync(string instanceId, CancellationToken ct = default)
    {
        if (_instances.TryGetValue(instanceId, out var existing))
            return Task.FromResult<WorkflowInstance?>(existing.Snapshot());
        return Task.FromResult<WorkflowInstance?>(null);
    }

    public Task<WorkflowInstance?> GetByWaitingHumanTaskId(
        string humanTaskId, CancellationToken ct = default)
    {
        var matches = _instances.Values
            .Where(i => i.Status == WorkflowInstanceStatus.Suspended &&
                        i.WaitingHumanTaskId == humanTaskId)
            .ToList();

        if (matches.Count > 1)
            throw new WorkflowCorrelationException(
                $"Multiple suspended instances found for HumanTask '{humanTaskId}'.");

        return Task.FromResult(matches.SingleOrDefault()?.Snapshot());
    }

    public IReadOnlyList<WorkflowInstance> GetAll()
        => _instances.Values.Select(instance => instance.Snapshot()).ToArray();

    public bool TryRemove(string instanceId)
        => _instances.TryRemove(instanceId, out _);
}
