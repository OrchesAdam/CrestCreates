using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using SourceRange = CrestCreates.Agent.Memory.Abstractions.SourceRange;

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

        // If the source ref specifies an event range, validate it against the same
        // contract the Expander uses. Invalid range = resource not found (fail-closed).
        var events = (IReadOnlyList<AgentTaskEvent>)task.Events;
        if (sourceRef is not null)
        {
            if (!SourceRange.TryResolve(sourceRef, events.Count, out var start, out var end))
                return null;

            if (start.HasValue)
            {
                events = events
                    .Skip(start.Value)
                    .Take(end!.Value - start.Value + 1)
                    .ToArray();
            }
        }

        var descriptorRefs = events
            .SelectMany(e => e.SourceRefs)
            .SelectMany(sr => sr.DescriptorRefs)
            .Distinct()
            .OrderBy(r => r.Namespace, StringComparer.Ordinal)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Version)
            .ToArray();

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = descriptorRefs,
            TenantId = task.TenantId
        };
    }
}
