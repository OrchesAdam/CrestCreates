using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Runtime.Persistence.InMemory.Kernel;

internal sealed class InMemoryRuntimePersistenceState
{
    public Dictionary<RuntimeInstanceKey, WorkflowInstance> Workflows { get; } = new();
    public Dictionary<RuntimeInstanceKey, HumanTaskInstance> HumanTasks { get; } = new();
    public Dictionary<string, (DescriptorSnapshot Snapshot, string Fingerprint)> Snapshots { get; } = new(StringComparer.Ordinal);
    public Dictionary<(RuntimeTenantScope Scope, string Operation), WorkflowSuspensionReceipt> Receipts { get; } = new();

    public InMemoryRuntimePersistenceState Clone()
    {
        var clone = new InMemoryRuntimePersistenceState();
        foreach (var (key, value) in Workflows)
            clone.Workflows[key] = value.Snapshot();
        foreach (var (key, value) in HumanTasks)
            clone.HumanTasks[key] = value.Snapshot();
        foreach (var (key, value) in Snapshots)
            clone.Snapshots[key] = (value.Snapshot.Snapshot(), value.Fingerprint);
        foreach (var (key, value) in Receipts)
            clone.Receipts[key] = value;
        return clone;
    }
}
