using System.Collections.Concurrent;
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
}
