using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Agent.Memory.Projection.Security;

internal sealed class CandidateResourceClosureProvider : IAgentMemoryResourceClosureProvider
{
    private readonly IAgentMemoryStore _store;
    private readonly ITenantContext _tenantContext;

    public CandidateResourceClosureProvider(
        IAgentMemoryStore store,
        ITenantContext tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    public string ResourceKind => AgentMemoryResourceKind.Candidate.ToString();

    public async ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (tenantId is null) return null;

        var candidate = await _store.GetCandidateAsync(tenantId, resourceId, cancellationToken);
        if (candidate is null) return null;

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = candidate.DescriptorRefs ?? Array.Empty<DescriptorRef>(),
            TenantId = candidate.TenantId
        };
    }
}
