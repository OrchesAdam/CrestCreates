using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

internal sealed class TaskEventResourceClosureProvider : IAgentMemoryResourceClosureProvider
{
    private readonly IAgentTaskHistoryStore _store;

    public TaskEventResourceClosureProvider(
        IAgentTaskHistoryStore store)
    {
        _store = store;
    }

    public string ResourceKind => AgentMemoryResourceKind.TaskEvent.ToString();

    public async ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string tenantId,
        string resourceId,
        AgentContextSourceRef? sourceRef = null,
        CancellationToken cancellationToken = default)
    {
        // resourceId is the TaskId that owns the event(s).
        var task = await _store.GetTaskAsync(tenantId, resourceId, cancellationToken);
        if (task is null) return null;

        // Filter events by RangeStart/RangeEnd if specified.
        // This must match the Expander's range validation and slicing logic exactly.
        var events = task.Events.AsEnumerable();
        if (sourceRef is { RangeStart: not null } || sourceRef is { RangeEnd: not null })
        {
            var start = sourceRef.RangeStart ?? 0;
            var end = sourceRef.RangeEnd ?? events.Count() - 1;
            events = events.Skip(start).Take(end - start + 1);
        }

        var descriptorRefs = events
            .SelectMany(e => e.SourceRefs)
            .SelectMany(sr => sr.DescriptorRefs)
            .Distinct()
            .ToArray();

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = descriptorRefs,
            TenantId = task.TenantId
        };
    }
}
