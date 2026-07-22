using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Agent.Memory.Projection.Security;

internal sealed class MemoryResourceClosureProvider : IAgentMemoryResourceClosureProvider
{
    private readonly IAgentMemoryStore _store;
    private readonly ITenantContext _tenantContext;

    public MemoryResourceClosureProvider(
        IAgentMemoryStore store,
        ITenantContext tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    public string ResourceKind => AgentMemoryResourceKind.Memory.ToString();

    public async ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (tenantId is null) return null;

        var memory = await _store.GetMemoryAsync(tenantId, resourceId, cancellationToken);
        if (memory is null) return null;

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = memory.DescriptorRefs ?? Array.Empty<DescriptorRef>(),
            TenantId = memory.TenantId
        };
    }
}
