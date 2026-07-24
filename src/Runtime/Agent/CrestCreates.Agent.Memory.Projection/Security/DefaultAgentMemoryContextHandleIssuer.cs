using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// Context handle issuer. Loads the trusted context from store, computes its
/// effective descriptor closure, validates closed-world scope constraints,
/// and issues the handle through the artifact coordinator.
/// </summary>
internal sealed class DefaultAgentMemoryContextHandleIssuer : IAgentMemoryContextHandleIssuer
{
    private readonly IAgentMemoryAccessArtifactCoordinator _coordinator;
    private readonly IAgentMemoryAccessScopeProvider _scopeProvider;
    private readonly IAgentMemoryArtifactLifetimePolicy _lifetimePolicy;
    private readonly IAgentCompressedContextStore _contextStore;
    private readonly TimeProvider _timeProvider;

    public DefaultAgentMemoryContextHandleIssuer(
        IAgentMemoryAccessArtifactCoordinator coordinator,
        IAgentMemoryAccessScopeProvider scopeProvider,
        IAgentMemoryArtifactLifetimePolicy lifetimePolicy,
        IAgentCompressedContextStore contextStore,
        TimeProvider timeProvider)
    {
        _coordinator = coordinator;
        _scopeProvider = scopeProvider;
        _lifetimePolicy = lifetimePolicy;
        _contextStore = contextStore;
        _timeProvider = timeProvider;
    }

    public async ValueTask<AgentMemoryContextHandleIssueResult> IssueForCallerAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        string trustedContextId,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate trustedContextId
        RequireIdentity(trustedContextId, nameof(trustedContextId));

        // 2. Load context from store using explicit principal tenant
        var context = await _contextStore.GetCompressedContextAsync(
            principal.TenantId, trustedContextId, cancellationToken);
        if (context is null)
            throw new InvalidOperationException($"Context not found: {trustedContextId}");

        // 3. Validate context tenant
        if (!string.Equals(context.TenantId, principal.TenantId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Cross-tenant context: {trustedContextId}");

        // 4. Resolve scope
        var scope = await _scopeProvider.ResolveAsync(principal, cancellationToken);

        // 5. Compute actual effective descriptor closure from context
        var effectiveClosure = ComputeEffectiveClosure(context);

        // 6. Closed-world scope validation: all closure refs must be in scope
        if (effectiveClosure.Count > 0)
        {
            var scopeSet = new HashSet<DescriptorRef>(scope.VisibleDescriptorRefs);
            if (!effectiveClosure.All(r => scopeSet.Contains(r)))
                throw new InvalidOperationException(
                    $"Context descriptor closure exceeds scope visibility");
        }
        else
        {
            // Unscoped context: scope must allow unscoped
            if (!scope.AllowUnscopedMemory)
                throw new InvalidOperationException(
                    $"Unscoped context not allowed by scope");
        }

        // 7. Compute IsUnscoped from actual closure (not from scope.AllowUnscopedMemory)
        var isUnscoped = effectiveClosure.Count == 0;

        // 8. Issue handle with actual closure
        var now = _timeProvider.GetUtcNow();
        var lifetime = _lifetimePolicy.GetHandleLifetime(principal, origin, scope, "context-handle");
        var handleId = Guid.NewGuid().ToString("N");
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = handleId,
            ResourceKind = AgentMemoryResourceKind.Context, // Fixed
            ResourceId = trustedContextId, // From trusted input, not arbitrary
            Principal = principal,
            ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope),
            RequiredDescriptorRefs = effectiveClosure, // Actual closure, not scope refs
            IsUnscoped = isUnscoped,
            IssuingOperationId = origin.OperationId,
            IssuedAt = now,
            ExpiresAt = now + lifetime,
        };

        // 9. Route through Coordinator
        var prepared = await _coordinator.PrepareAsync(
            principal, origin, scope, "context-handle",
            preparationOrdinal: 0,
            handles: [handle],
            grants: [],
            cancellationToken);

        // 10. Use prepared.Handles for result
        var issuedHandle = prepared.Handles?.Handles.FirstOrDefault()
            ?? throw new InvalidOperationException("Context handle issuance failed");

        return new AgentMemoryContextHandleIssueResult
        {
            HandleId = issuedHandle.HandleId,
            ExpiresAt = issuedHandle.ExpiresAt,
            CompensationToken = prepared.CompensationToken
        };
    }

    /// <summary>
    /// Computes the effective descriptor closure from a compressed context:
    /// aggregates all DescriptorRefs from all blocks and their source refs.
    /// Delegates to EffectiveClosureHelper for canonical ordering.
    /// </summary>
    private static IReadOnlyList<DescriptorRef> ComputeEffectiveClosure(
        AgentCompressedContext context)
    {
        return EffectiveClosureHelper.ComputeEffectiveClosureFromBlocks(context.Blocks);
    }

    private static void RequireIdentity(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{paramName} must be a valid identity.");
    }
}
