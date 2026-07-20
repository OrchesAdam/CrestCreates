using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

/// <summary>
/// Single scope revalidation point for resource handles and source grants.
/// Handles are capabilities, not permanent authorization: every use reloads
/// the resource graph and compares it with the handle snapshot.
/// </summary>
public sealed class AgentMemoryResourceHandleResolver : IAgentMemoryResourceHandleResolver, IAgentMemorySourceGrantResolver
{
    private readonly IAgentMemoryResourceHandleStore _handles;
    private readonly IAgentMemorySourceGrantStore _grants;
    private readonly IAgentMemoryStore _memory;
    private readonly IAgentCompressedContextStore _contexts;
    private readonly TimeProvider _time;

    public AgentMemoryResourceHandleResolver(
        IAgentMemoryResourceHandleStore handles,
        IAgentMemorySourceGrantStore grants,
        IAgentMemoryStore memory,
        IAgentCompressedContextStore contexts,
        TimeProvider time)
    {
        _handles = handles;
        _grants = grants;
        _memory = memory;
        _contexts = contexts;
        _time = time;
    }

    public async ValueTask<AgentMemoryResolvedResourceHandle?> ResolveAsync(
        string handleId,
        AgentMemoryResourceKind expectedKind,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        CancellationToken cancellationToken = default)
    {
        var handle = await _handles.GetAsync(handleId, cancellationToken).ConfigureAwait(false);
        if (handle is null || handle.ResourceKind != expectedKind || handle.Principal != principal
            || handle.State != AgentMemorySecurityArtifactState.Active || handle.ExpiresAt <= _time.GetUtcNow())
            return null;

        object? resource = null;
        IReadOnlyList<DescriptorRef> effectiveRefs = Array.Empty<DescriptorRef>();
        switch (expectedKind)
        {
            case AgentMemoryResourceKind.Memory:
                resource = await _memory.GetMemoryAsync(principal.TenantId, handle.ResourceId, cancellationToken).ConfigureAwait(false);
                if (resource is AgentMemoryItem memory)
                    effectiveRefs = EffectiveRefs(memory.DescriptorRefs, memory.SourceRefs.SelectMany(item => item.DescriptorRefs));
                break;
            case AgentMemoryResourceKind.Candidate:
                resource = await _memory.GetCandidateAsync(principal.TenantId, handle.ResourceId, cancellationToken).ConfigureAwait(false);
                if (resource is AgentMemoryCandidate candidate)
                    effectiveRefs = EffectiveRefs(candidate.DescriptorRefs, candidate.SourceRefs.SelectMany(item => item.DescriptorRefs));
                break;
            case AgentMemoryResourceKind.Context:
                resource = await _contexts.GetCompressedContextAsync(principal.TenantId, handle.ResourceId, cancellationToken).ConfigureAwait(false);
                if (resource is AgentCompressedContext context)
                    effectiveRefs = EffectiveRefs(context.Blocks.SelectMany(block => block.SourceRefs.SelectMany(item => item.DescriptorRefs)));
                break;
            case AgentMemoryResourceKind.ConversationHistory:
            case AgentMemoryResourceKind.TaskHistory:
                break;
            default:
                return null;
        }

        if ((expectedKind is AgentMemoryResourceKind.Memory or AgentMemoryResourceKind.Candidate
            or AgentMemoryResourceKind.Context) && resource is null)
            return null;
        if ((expectedKind is AgentMemoryResourceKind.ConversationHistory or AgentMemoryResourceKind.TaskHistory)
            && !string.Equals(handle.ScopeFingerprint, AgentMemoryScopeFingerprint.Compute(scope, principal), StringComparison.Ordinal))
            return null;
        if (expectedKind is AgentMemoryResourceKind.ConversationHistory or AgentMemoryResourceKind.TaskHistory)
            return new AgentMemoryResolvedResourceHandle { Handle = handle };
        if (!IsCurrentScope(handle, effectiveRefs, scope, principal))
            return null;
        return new AgentMemoryResolvedResourceHandle
        {
            Handle = handle,
            Resource = resource,
            EffectiveDescriptorRefs = effectiveRefs
        };
    }

    public async ValueTask<AgentMemorySourceGrant?> ResolveAsync(
        string grantId,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        CancellationToken cancellationToken = default)
    {
        var grant = await _grants.GetAsync(grantId, cancellationToken).ConfigureAwait(false);
        if (grant is null || grant.Principal != principal
            || grant.State != AgentMemorySecurityArtifactState.Active
            || grant.ExpiresAt <= _time.GetUtcNow()
            || !string.Equals(grant.SourceRef.TenantId, principal.TenantId, StringComparison.Ordinal))
            return null;
        if (!IsGrantCurrentScope(grant, scope, principal))
            return null;
        return grant;
    }

    private static bool IsGrantCurrentScope(
        AgentMemorySourceGrant grant,
        AgentMemoryToolAccessScope scope,
        AgentMemoryToolPrincipal principal)
    {
        var required = EffectiveRefs(grant.RequiredDescriptorRefs);
        var sourceRefs = EffectiveRefs(grant.SourceRef.DescriptorRefs);
        if (!string.Equals(grant.ScopeFingerprint, AgentMemoryScopeFingerprint.Compute(scope, principal), StringComparison.Ordinal)
            || grant.IsUnscoped != (required.Count == 0)
            || required.Any(item => item.Version is not > 0)
            || sourceRefs.Any(item => !required.Contains(item)))
            return false;
        if (required.Count == 0)
            return scope.AllowUnscopedMemory;
        var visible = scope.VisibleDescriptorRefs.ToHashSet();
        return required.All(visible.Contains);
    }

    private static IReadOnlyList<DescriptorRef> EffectiveRefs(params IEnumerable<DescriptorRef>[] groups)
        => EffectiveRefs(groups.SelectMany(item => item));

    private static IReadOnlyList<DescriptorRef> EffectiveRefs(IEnumerable<DescriptorRef> refs)
        => refs.Distinct().OrderBy(item => item.Namespace, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Version).ToArray();

    private static bool IsCurrentScope(
        AgentMemoryResourceHandle handle,
        IReadOnlyList<DescriptorRef> effectiveRefs,
        AgentMemoryToolAccessScope scope,
        AgentMemoryToolPrincipal principal)
        => IsCurrentScope(handle.ScopeFingerprint, handle.IsUnscoped, handle.RequiredDescriptorRefs,
            effectiveRefs, scope, principal);

    private static bool IsCurrentScope(
        string scopeFingerprint,
        bool isUnscoped,
        IReadOnlyList<DescriptorRef> requiredRefs,
        IReadOnlyList<DescriptorRef> effectiveRefs,
        AgentMemoryToolAccessScope scope,
        AgentMemoryToolPrincipal principal)
    {
        if (!string.Equals(scopeFingerprint, AgentMemoryScopeFingerprint.Compute(scope, principal), StringComparison.Ordinal))
            return false;
        if (!DescriptorRefsEqual(requiredRefs, effectiveRefs))
            return false;
        if (effectiveRefs.Any(item => item.Version is not > 0))
            return false;
        if (isUnscoped != (effectiveRefs.Count == 0))
            return false;
        if (effectiveRefs.Count == 0)
            return scope.AllowUnscopedMemory;
        var visible = scope.VisibleDescriptorRefs.ToHashSet();
        return effectiveRefs.All(visible.Contains);
    }

    private static bool DescriptorRefsEqual(IReadOnlyList<DescriptorRef> left, IReadOnlyList<DescriptorRef> right)
        => left.OrderBy(item => item.Namespace, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Version)
            .SequenceEqual(right.OrderBy(item => item.Namespace, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Version));
}

internal static class AgentMemoryScopeFingerprint
{
    public static string Compute(AgentMemoryToolAccessScope scope, AgentMemoryToolPrincipal principal)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"memory-scope-v2|{principal.TenantId}|{scope.AllowUnscopedMemory}|{string.Join('|', scope.VisibleDescriptorRefs.OrderBy(item => item.Namespace, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Version).Select(item => $"{item.Namespace}:{item.Id}:{item.Version}"))}"))).ToLowerInvariant();
}
