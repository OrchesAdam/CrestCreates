namespace CrestCreates.Agent.Memory.Tools;

internal sealed class AgentMemoryHistoryResourceHandleIssuer : IAgentMemoryHistoryResourceHandleIssuer
{
    private readonly IAgentMemoryHistoryAccessAuthorizer _authorizer;
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemorySecurityArtifactCoordinator _coordinator;
    private readonly TimeProvider _time;

    public AgentMemoryHistoryResourceHandleIssuer(
        IAgentMemoryHistoryAccessAuthorizer authorizer,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemorySecurityArtifactCoordinator coordinator,
        TimeProvider? time = null)
    {
        _authorizer = authorizer;
        _scopeProvider = scopeProvider;
        _coordinator = coordinator;
        _time = time ?? TimeProvider.System;
    }

    public async ValueTask<string> IssueAsync(
        AgentMemoryHostArtifactBatchKey hostBatchKey,
        AgentMemoryToolPrincipal principal,
        AgentMemoryHistorySourceKind sourceKind,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostBatchKey.HostOperationId)
            || hostBatchKey.OperationFingerprint is null
            || string.IsNullOrWhiteSpace(hostBatchKey.OperationFingerprint.Value)
            || string.IsNullOrWhiteSpace(hostBatchKey.ArtifactPurpose)
            || sourceKind == AgentMemoryHistorySourceKind.Unknown
            || string.IsNullOrWhiteSpace(sourceId))
            throw new InvalidOperationException("A complete trusted Host history batch key is required.");
        var scope = await _scopeProvider.ResolveAsync(principal, cancellationToken).ConfigureAwait(false);
        if (!await _authorizer.IsAuthorizedAsync(principal, scope, sourceKind, sourceId, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("History source is unavailable.");
        var now = _time.GetUtcNow();
        var resourceKind = sourceKind == AgentMemoryHistorySourceKind.Conversation
            ? AgentMemoryResourceKind.ConversationHistory : AgentMemoryResourceKind.TaskHistory;
        var handle = new AgentMemoryResourceHandle
        {
            HandleId = AgentMemorySecurityArtifactIdGenerator.Create("hnd"),
            ResourceKind = resourceKind,
            ResourceId = sourceId,
            Principal = principal,
            ScopeFingerprint = AgentMemoryToolScopeFingerprint.Compute(scope, principal),
            IsUnscoped = false,
            IssuingInvocationId = hostBatchKey.HostOperationId,
            IssuedAt = now,
            ExpiresAt = now.Add(scope.ResourceHandleLifetime)
        };
        var prepared = await _coordinator.PrepareForHostAsync(
            hostBatchKey, principal, scope, sourceKind, sourceId, [handle], [], cancellationToken).ConfigureAwait(false);
        if (prepared.Handles is null || prepared.Handles.Handles.Count != 1)
            throw new InvalidOperationException("History handle preparation returned an invalid result.");
        return prepared.Handles.Handles[0].HandleId;
    }

}
