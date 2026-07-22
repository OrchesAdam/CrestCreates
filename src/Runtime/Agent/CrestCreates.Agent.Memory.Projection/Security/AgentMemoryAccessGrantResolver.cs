using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// Projection-neutral grant resolver. Validates Principal, scope fingerprint,
/// descriptor closure, live source resource existence, and tenant boundary on every resolution.
/// </summary>
internal sealed class AgentMemoryAccessGrantResolver : IAgentMemoryAccessGrantResolver
{
    private readonly IAgentMemoryAccessGrantStore _grantStore;
    private readonly TimeProvider _timeProvider;
    private readonly IAgentMemoryCurrentClosureProvider _closureProvider;

    public AgentMemoryAccessGrantResolver(
        IAgentMemoryAccessGrantStore grantStore,
        TimeProvider timeProvider,
        IAgentMemoryCurrentClosureProvider closureProvider)
    {
        _grantStore = grantStore;
        _timeProvider = timeProvider;
        _closureProvider = closureProvider;
    }

    public async ValueTask<AgentMemoryAccessSourceGrant?> ResolveAsync(
        string grantId,
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        CancellationToken cancellationToken = default)
    {
        var grant = await _grantStore.GetAsync(grantId, cancellationToken);
        if (grant is null) return null;

        // Full Principal record equality
        if (grant.Principal != principal) return null;

        // Active state
        if (grant.State != AgentMemorySecurityArtifactState.Active) return null;
        if (grant.ExpiresAt <= _timeProvider.GetUtcNow()) return null;

        // Scope fingerprint must match current scope
        var currentFingerprint = AgentMemoryScopeFingerprint.Compute(scope);
        if (grant.ScopeFingerprint != currentFingerprint) return null;

        // Descriptor closure
        if (grant.IsUnscoped)
        {
            if (!scope.AllowUnscopedMemory) return null;
        }
        else
        {
            if (grant.RequiredDescriptorRefs is { Count: > 0 })
            {
                var visibleSet = new HashSet<DescriptorRef>(scope.VisibleDescriptorRefs);
                if (!grant.RequiredDescriptorRefs.All(r => visibleSet.Contains(r)))
                    return null;
            }
        }

        // Tenant boundary
        if (grant.Principal.TenantId != scope.TenantId) return null;

        // Live closure revalidation for the source resource
        var sourceKind = grant.SourceRef.SourceKind switch
        {
            AgentSourceKind.CompressedContextBlock => AgentMemoryResourceKind.Context,
            AgentSourceKind.ConversationTurn => AgentMemoryResourceKind.ConversationHistory,
            AgentSourceKind.TaskRecord => AgentMemoryResourceKind.TaskHistory,
            AgentSourceKind.MemoryItem => AgentMemoryResourceKind.Memory,
            _ => AgentMemoryResourceKind.Context
        };
        var currentClosure = await _closureProvider.GetCurrentClosureAsync(
            sourceKind, grant.SourceRef.SourceId, cancellationToken);
        if (currentClosure is null) return null;

        // SourceRef tenant must match current resource tenant
        if (currentClosure.TenantId != principal.TenantId) return null;

        // Current closure must be a superset of the grant's required refs
        if (!grant.IsUnscoped && grant.RequiredDescriptorRefs is { Count: > 0 })
        {
            var currentSet = new HashSet<DescriptorRef>(currentClosure.CurrentDescriptorRefs);
            if (!grant.RequiredDescriptorRefs.All(r => currentSet.Contains(r)))
                return null;
        }

        return grant;
    }
}
