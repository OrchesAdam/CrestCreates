using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// Projection-neutral handle resolver. Validates Principal, scope fingerprint,
/// descriptor closure, live resource existence, and tenant boundary on every resolution.
/// </summary>
internal sealed class AgentMemoryAccessHandleResolver : IAgentMemoryAccessHandleResolver
{
    private readonly IAgentMemoryAccessHandleStore _handleStore;
    private readonly TimeProvider _timeProvider;
    private readonly IAgentMemoryCurrentClosureProvider _closureProvider;

    public AgentMemoryAccessHandleResolver(
        IAgentMemoryAccessHandleStore handleStore,
        TimeProvider timeProvider,
        IAgentMemoryCurrentClosureProvider closureProvider)
    {
        _handleStore = handleStore;
        _timeProvider = timeProvider;
        _closureProvider = closureProvider;
    }

    public async ValueTask<AgentMemoryAccessResolvedResource?> ResolveAsync(
        string handleId,
        AgentMemoryResourceKind expectedKind,
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        CancellationToken cancellationToken = default)
    {
        var handle = await _handleStore.GetAsync(handleId, cancellationToken);
        if (handle is null) return null;

        // Full Principal record equality
        if (handle.Principal != principal) return null;

        // Kind match
        if (handle.ResourceKind != expectedKind) return null;

        // Active state (read-purified: store returns expired state view without persisting)
        if (handle.State != AgentMemorySecurityArtifactState.Active) return null;
        if (handle.ExpiresAt <= _timeProvider.GetUtcNow()) return null;

        // Scope fingerprint must match current scope
        var currentFingerprint = AgentMemoryScopeFingerprint.Compute(scope);
        if (handle.ScopeFingerprint != currentFingerprint) return null;

        // Descriptor closure: all required refs must be visible in current scope
        // History resources (ConversationHistory, TaskHistory) are resource-bound
        // (constrained by ResourceId, Tenant, Principal, ScopeFingerprint, existence) —
        // they don't have descriptor closures and don't require AllowUnscopedMemory.
        // TaskEvent is Grant-only — cannot be resolved as a Handle.
        if (!AgentMemoryHandleGrantMatrix.IsHandleSupported(handle.ResourceKind))
            return null;

        var isHistoryResource = AgentMemoryHandleGrantMatrix.IsHistoryHandleKind(handle.ResourceKind);
        if (isHistoryResource)
        {
            // History resources: closure check is existence + tenant + scope fingerprint only
            // (already verified above: currentClosure != null && tenantId match && fingerprint match)
        }
        else if (handle.IsUnscoped)
        {
            if (!scope.AllowUnscopedMemory) return null;
        }
        else
        {
            if (handle.RequiredDescriptorRefs is { Count: > 0 })
            {
                var visibleSet = new HashSet<DescriptorRef>(scope.VisibleDescriptorRefs);
                if (!handle.RequiredDescriptorRefs.All(r => visibleSet.Contains(r)))
                    return null;
            }
        }

        // Tenant boundary
        if (handle.Principal.TenantId != scope.TenantId) return null;

        // Live closure revalidation: resource must still exist with compatible descriptors
        // Use explicit principal tenant, not ambient context
        var currentClosure = await _closureProvider.GetCurrentClosureAsync(
            handle.ResourceKind, principal.TenantId, handle.ResourceId,
            sourceRef: null, cancellationToken: cancellationToken);
        if (currentClosure is null) return null; // Resource deleted

        // Resource tenant must match principal tenant
        if (currentClosure.TenantId != principal.TenantId) return null;

        // ALWAYS compare issued closure with current closure.
        // Exception: ConversationHistory and TaskHistory are raw history resources
        // that don't have descriptor closures — only verify existence and tenant.
        // TaskEvent is Grant-only and cannot reach this path.
        if (AgentMemoryHandleGrantMatrix.IsHistoryHandleKind(handle.ResourceKind))
        {
            // History resources: closure check is existence + tenant only
            // (already verified above: currentClosure != null && tenantId match)
        }
        else
        {
            var issuedRefs = handle.RequiredDescriptorRefs ?? Array.Empty<DescriptorRef>();
            var currentRefs = currentClosure.CurrentDescriptorRefs;
            if (!CanonicalRefSetEquals(issuedRefs, currentRefs))
                return null;
        }

        return new AgentMemoryAccessResolvedResource
        {
            Handle = handle,
            EffectiveDescriptorRefs = handle.RequiredDescriptorRefs ?? Array.Empty<DescriptorRef>()
        };
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
