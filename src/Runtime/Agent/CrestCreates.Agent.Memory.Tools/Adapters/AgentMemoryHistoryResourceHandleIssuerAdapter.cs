using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Tools.Adapters;

/// <summary>
/// Adapter: old IAgentMemoryHistoryResourceHandleIssuer → new IAgentMemoryContextHandleIssuer
/// + direct coordinator usage for non-Context history resources (Conversation, Task).
/// </summary>
internal sealed class AgentMemoryHistoryResourceHandleIssuerAdapter : IAgentMemoryHistoryResourceHandleIssuer
{
    private readonly IAgentMemoryContextHandleIssuer _canonical;
    private readonly IAgentMemoryAccessArtifactCoordinator _coordinator;
    private readonly IAgentMemoryAccessScopeProvider _scopeProvider;
    private readonly IAgentMemoryArtifactLifetimePolicy _lifetimePolicy;
    private readonly TimeProvider _timeProvider;

    public AgentMemoryHistoryResourceHandleIssuerAdapter(
        IAgentMemoryContextHandleIssuer canonical,
        IAgentMemoryAccessArtifactCoordinator coordinator,
        IAgentMemoryAccessScopeProvider scopeProvider,
        IAgentMemoryArtifactLifetimePolicy lifetimePolicy,
        TimeProvider timeProvider)
    {
        _canonical = canonical;
        _coordinator = coordinator;
        _scopeProvider = scopeProvider;
        _lifetimePolicy = lifetimePolicy;
        _timeProvider = timeProvider;
    }

    public async ValueTask<string> IssueAsync(
        AgentMemoryHostArtifactBatchKey hostBatchKey,
        AgentMemoryToolPrincipal principal,
        AgentMemoryHistorySourceKind sourceKind,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (sourceKind == AgentMemoryHistorySourceKind.Unknown)
            throw new InvalidOperationException("History source kind must not be Unknown.");
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("Source ID must be a valid identity.", nameof(sourceId));

        var newPrincipal = new AgentMemoryAccessPrincipal
        {
            TenantId = principal.TenantId,
            UserId = principal.UserId,
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = principal.AgentId,
            SecurityContextId = principal.ExecutionId,
        };

        var origin = new AgentMemoryArtifactOrigin
        {
            Kind = AgentMemoryArtifactOriginKind.TrustedHostOperation,
            OperationId = hostBatchKey.HostOperationId,
            BindingHash = hostBatchKey.OperationFingerprint,
        };

        // For Conversation/Task history resources, issue through the coordinator directly
        var resourceKind = sourceKind switch
        {
            AgentMemoryHistorySourceKind.Conversation => AgentMemoryResourceKind.ConversationHistory,
            AgentMemoryHistorySourceKind.Task => AgentMemoryResourceKind.TaskHistory,
            _ => throw new InvalidOperationException($"Unexpected history source kind: {sourceKind}")
        };

        var scope = await _scopeProvider.ResolveAsync(newPrincipal, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var lifetime = _lifetimePolicy.GetHandleLifetime(newPrincipal, origin, scope, "history-handle");

        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = Guid.NewGuid().ToString("N"),
            ResourceKind = resourceKind,
            ResourceId = sourceId,
            Principal = newPrincipal,
            ScopeFingerprint = ComputeScopeFingerprint(scope),
            RequiredDescriptorRefs = [],
            IsUnscoped = false, // History is resource-bound, not unscoped; exempt from consistency formula
            IssuingOperationId = origin.OperationId,
            IssuedAt = now,
            ExpiresAt = now + lifetime,
        };

        var prepared = await _coordinator.PrepareAsync(
            newPrincipal, origin, scope, hostBatchKey.ArtifactPurpose ?? "history-handle",
            preparationOrdinal: 0,
            handles: [handle],
            grants: [],
            cancellationToken);

        var issuedHandle = prepared.Handles?.Handles.FirstOrDefault()
            ?? throw new InvalidOperationException("History handle issuance failed");

        return issuedHandle.HandleId;
    }

    private static string ComputeScopeFingerprint(AgentMemoryAccessScope scope)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"projection-scope-v1|{scope.TenantId}|{scope.AllowUnscopedMemory}|");
        var ordered = scope.VisibleDescriptorRefs
            .OrderBy(r => r.Namespace, StringComparer.Ordinal)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Version);
        sb.Append(string.Join('|', ordered.Select(r => $"{r.Namespace}:{r.Id}:{r.Version}")));
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}
