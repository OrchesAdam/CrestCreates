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
        // History grants (ConversationTurn, TaskRecord) are resource-bound —
        // they don't require AllowUnscopedMemory.
        AgentMemoryResourceKind sourceKind;
        try
        {
            sourceKind = AgentMemorySourceKindSupport.ToResourceKind(grant.SourceRef.SourceKind);
        }
        catch (InvalidOperationException)
        {
            // Fail-closed: unknown source kind is rejected
            return null;
        }
        var isHistorySource = AgentMemoryHandleGrantMatrix.IsHistoryHandleKind(sourceKind)
            || sourceKind == AgentMemoryResourceKind.TaskEvent;
        if (isHistorySource)
        {
            // History grants: no descriptor closure check, no AllowUnscopedMemory requirement
        }
        else if (grant.IsUnscoped)
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

        // Live closure revalidation for the source resource (sourceKind already resolved above)
        var currentClosure = await _closureProvider.GetCurrentClosureAsync(
            sourceKind, principal.TenantId, grant.SourceRef.SourceId,
            sourceRef: grant.SourceRef, cancellationToken);
        if (currentClosure is null) return null;

        // SourceRef tenant must match current resource tenant
        if (currentClosure.TenantId != principal.TenantId) return null;

        // ALWAYS compare issued closure with current closure.
        // IsUnscoped means issued closure was empty — still must match exactly.
        {
            var issuedRefs = grant.RequiredDescriptorRefs ?? Array.Empty<DescriptorRef>();
            var currentRefs = currentClosure.CurrentDescriptorRefs;
            if (!CanonicalRefSetEquals(issuedRefs, currentRefs))
                return null;
        }

        return grant;
    }

    private static bool CanonicalRefSetEquals(
        IReadOnlyList<DescriptorRef> a, IReadOnlyList<DescriptorRef> b)
    {
        if (a.Count != b.Count) return false;
        var setA = new HashSet<DescriptorRef>(a);
        var setB = new HashSet<DescriptorRef>(b);
        return setA.SetEquals(setB);
    }
}
