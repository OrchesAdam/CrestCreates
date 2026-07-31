using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Runtime.Persistence.InMemory.Kernel;

internal sealed class InMemoryRuntimePersistenceState
{
    public Dictionary<RuntimeInstanceKey, WorkflowInstance> Workflows { get; } = new();
    public Dictionary<RuntimeInstanceKey, HumanTaskInstance> HumanTasks { get; } = new();

    public InMemoryRuntimePersistenceState Clone()
    {
        var clone = new InMemoryRuntimePersistenceState();
        foreach (var (key, value) in Workflows)
            clone.Workflows[key] = value.Snapshot();
        foreach (var (key, value) in HumanTasks)
            clone.HumanTasks[key] = value.Snapshot();
        return clone;
    }
}
