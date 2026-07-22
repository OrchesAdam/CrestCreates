using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

internal sealed class CandidateResourceClosureProvider : IAgentMemoryResourceClosureProvider
{
    private readonly IAgentMemoryStore _store;

    public CandidateResourceClosureProvider(
        IAgentMemoryStore store)
    {
        _store = store;
    }

    public string ResourceKind => AgentMemoryResourceKind.Candidate.ToString();

    public async ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        string tenantId,
        string resourceId,
        AgentContextSourceRef? sourceRef = null,
        CancellationToken cancellationToken = default)
    {
        var candidate = await _store.GetCandidateAsync(tenantId, resourceId, cancellationToken);
        if (candidate is null) return null;

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = EffectiveClosureHelper.ComputeEffectiveClosure(candidate.DescriptorRefs, candidate.SourceRefs),
            TenantId = candidate.TenantId
        };
    }
}
