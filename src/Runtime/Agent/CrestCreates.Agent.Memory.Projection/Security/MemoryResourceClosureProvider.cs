using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

internal sealed class MemoryResourceClosureProvider : IAgentMemoryResourceClosureProvider
{
    private readonly IAgentMemoryStore _store;

    public MemoryResourceClosureProvider(
        IAgentMemoryStore store)
    {
        _store = store;
    }

    public string ResourceKind => AgentMemoryResourceKind.Memory.ToString();

    public async ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string tenantId,
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var memory = await _store.GetMemoryAsync(tenantId, resourceId, cancellationToken);
        if (memory is null) return null;

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = memory.DescriptorRefs ?? Array.Empty<DescriptorRef>(),
            TenantId = memory.TenantId
        };
    }
}
