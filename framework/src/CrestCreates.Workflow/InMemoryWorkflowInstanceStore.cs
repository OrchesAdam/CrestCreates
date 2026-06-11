using System.Collections.Concurrent;
using System.Linq;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class InMemoryWorkflowInstanceStore : IWorkflowInstanceStore
{
    private readonly ConcurrentDictionary<string, WorkflowInstance> _instances = new();

    public Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
        _instances[instance.InstanceId] = instance;
        return Task.CompletedTask;
    }

    public Task<WorkflowInstance?> GetAsync(string instanceId, CancellationToken ct = default)
    {
        _instances.TryGetValue(instanceId, out var instance);
        return Task.FromResult(instance);
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

        return Task.FromResult(matches.SingleOrDefault());
    }
}
