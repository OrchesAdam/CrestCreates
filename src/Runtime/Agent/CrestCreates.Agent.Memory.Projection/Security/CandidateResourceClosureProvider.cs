using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
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
        // RangePolicy: MemoryCandidate is NoRange — reject if sourceRef carries a range
        if (sourceRef is not null && !AgentMemoryHandleGrantMatrix.IsRangeAllowed(sourceRef))
            return null;

        var candidate = await _store.GetCandidateAsync(tenantId, resourceId, cancellationToken);
        if (candidate is null) return null;

        return new AgentMemoryCurrentClosure
        {
            CurrentDescriptorRefs = EffectiveClosureHelper.ComputeEffectiveClosure(candidate.DescriptorRefs, candidate.SourceRefs),
            TenantId = candidate.TenantId
        };
    }
}
