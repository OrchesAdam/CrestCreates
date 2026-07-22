using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Agent.Memory.Projection.Security;

internal sealed class ContextResourceClosureProvider : IAgentMemoryResourceClosureProvider
{
    private readonly IAgentCompressedContextStore _store;
    private readonly ITenantContext _tenantContext;

    public ContextResourceClosureProvider(
        IAgentCompressedContextStore store,
        ITenantContext tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    public string ResourceKind => AgentMemoryResourceKind.Context.ToString();

    public async ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (tenantId is null) return null;

        var context = await _store.GetCompressedContextAsync(tenantId, resourceId, cancellationToken);
        if (context is null) return null;

        var descriptorRefs = context.Blocks
            .SelectMany(b => b.SourceRefs)
            .SelectMany(sr => sr.DescriptorRefs)
            .Distinct()
            .ToArray();

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = descriptorRefs,
            TenantId = context.TenantId
        };
    }
}
