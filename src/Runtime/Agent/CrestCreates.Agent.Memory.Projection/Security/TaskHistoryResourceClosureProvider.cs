using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Agent.Memory.Projection.Security;

internal sealed class TaskHistoryResourceClosureProvider : IAgentMemoryResourceClosureProvider
{
    private readonly IAgentTaskHistoryStore _store;
    private readonly ITenantContext _tenantContext;

    public TaskHistoryResourceClosureProvider(
        IAgentTaskHistoryStore store,
        ITenantContext tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    public string ResourceKind => AgentMemoryResourceKind.TaskHistory.ToString();

    public async ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (tenantId is null) return null;

        var task = await _store.GetTaskAsync(tenantId, resourceId, cancellationToken);
        if (task is null) return null;

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = Array.Empty<DescriptorRef>(),
            TenantId = task.TenantId
        };
    }
}
