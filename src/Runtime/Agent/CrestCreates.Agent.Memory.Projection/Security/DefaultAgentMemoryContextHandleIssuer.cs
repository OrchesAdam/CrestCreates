using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// Context handle issuer. Routes through IAgentMemoryAccessArtifactCoordinator.PrepareAsync —
/// never directly accesses IAgentMemoryAccessHandleStore.
/// </summary>
internal sealed class DefaultAgentMemoryContextHandleIssuer : IAgentMemoryContextHandleIssuer
{
    private readonly IAgentMemoryAccessArtifactCoordinator _coordinator;
    private readonly IAgentMemoryAccessScopeProvider _scopeProvider;
    private readonly IAgentMemoryArtifactLifetimePolicy _lifetimePolicy;
    private readonly TimeProvider _timeProvider;

    public DefaultAgentMemoryContextHandleIssuer(
        IAgentMemoryAccessArtifactCoordinator coordinator,
        IAgentMemoryAccessScopeProvider scopeProvider,
        IAgentMemoryArtifactLifetimePolicy lifetimePolicy,
        TimeProvider timeProvider)
    {
        _coordinator = coordinator;
        _scopeProvider = scopeProvider;
        _lifetimePolicy = lifetimePolicy;
        _timeProvider = timeProvider;
    }

    public async ValueTask<AgentMemoryContextHandleIssueResult> IssueAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        string purpose,
        AgentMemoryResourceKind resourceKind,
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        // resourceId must be a valid identity — not empty or whitespace
        if (string.IsNullOrWhiteSpace(resourceId))
            throw new ArgumentException("ResourceId must be a valid identity.", nameof(resourceId));

        var scope = await _scopeProvider.ResolveAsync(principal, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var lifetime = _lifetimePolicy.GetHandleLifetime(principal, origin, scope, purpose);

        var handleId = Guid.NewGuid().ToString("N");
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = handleId,
            ResourceKind = resourceKind,
            ResourceId = resourceId,
            Principal = principal,
            ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope),
            RequiredDescriptorRefs = scope.VisibleDescriptorRefs,
            IsUnscoped = scope.AllowUnscopedMemory,
            IssuingOperationId = origin.OperationId,
            IssuedAt = now,
            ExpiresAt = now + lifetime,
        };

        var prepared = await _coordinator.PrepareAsync(
            principal, origin, scope, purpose,
            preparationOrdinal: 0,
            handles: [handle],
            grants: [],
            cancellationToken);

        var issuedHandle = prepared.Handles?.Handles.FirstOrDefault(h => h.HandleId == handleId)
            ?? prepared.Handles?.Handles.First();

        return new AgentMemoryContextHandleIssueResult
        {
            HandleId = issuedHandle!.HandleId,
            ExpiresAt = issuedHandle.ExpiresAt
        };
    }
}
