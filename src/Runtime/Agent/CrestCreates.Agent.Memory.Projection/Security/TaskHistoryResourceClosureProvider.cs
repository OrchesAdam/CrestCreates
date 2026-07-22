using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

internal sealed class TaskHistoryResourceClosureProvider : IAgentMemoryResourceClosureProvider
{
    private readonly IAgentTaskHistoryStore _store;

    public TaskHistoryResourceClosureProvider(
        IAgentTaskHistoryStore store)
    {
        _store = store;
    }

    public string ResourceKind => AgentMemoryResourceKind.TaskHistory.ToString();

    public async ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string tenantId,
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var task = await _store.GetTaskAsync(tenantId, resourceId, cancellationToken);
        if (task is null) return null;

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = Array.Empty<DescriptorRef>(),
            TenantId = task.TenantId
        };
    }
}
