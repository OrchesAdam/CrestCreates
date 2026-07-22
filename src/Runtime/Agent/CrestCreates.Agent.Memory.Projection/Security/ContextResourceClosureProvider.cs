using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

internal sealed class ContextResourceClosureProvider : IAgentMemoryResourceClosureProvider
{
    private readonly IAgentCompressedContextStore _store;

    public ContextResourceClosureProvider(
        IAgentCompressedContextStore store)
    {
        _store = store;
    }

    public string ResourceKind => AgentMemoryResourceKind.Context.ToString();

    public async ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string tenantId,
        string resourceId,
        AgentContextSourceRef? sourceRef = null,
        CancellationToken cancellationToken = default)
    {
        var context = await _store.GetCompressedContextAsync(tenantId, resourceId, cancellationToken);
        if (context is null) return null;

        var descriptorRefs = EffectiveClosureHelper.ComputeEffectiveClosureFromBlocks(context.Blocks);

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = descriptorRefs,
            TenantId = context.TenantId
        };
    }
}
