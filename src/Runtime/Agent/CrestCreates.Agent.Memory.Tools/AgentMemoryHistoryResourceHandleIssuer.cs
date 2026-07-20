using System.Security.Cryptography;
using System.Text;

namespace CrestCreates.Agent.Memory.Tools;

internal sealed class AgentMemoryHistoryResourceHandleIssuer : IAgentMemoryHistoryResourceHandleIssuer
{
    private readonly IAgentMemoryHistoryAccessAuthorizer _authorizer;
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemoryResourceHandleStore _store;
    private readonly TimeProvider _time;

    public AgentMemoryHistoryResourceHandleIssuer(
        IAgentMemoryHistoryAccessAuthorizer authorizer,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemoryResourceHandleStore store,
        TimeProvider? time = null)
    {
        _authorizer = authorizer;
        _scopeProvider = scopeProvider;
        _store = store;
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
            || string.IsNullOrWhiteSpace(hostBatchKey.OperationFingerprint)
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
            ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope, principal),
            IsUnscoped = false,
            IssuingInvocationId = hostBatchKey.HostOperationId,
            IssuedAt = now,
            ExpiresAt = now.Add(scope.ResourceHandleLifetime)
        };
        var batch = new AgentMemorySecurityArtifactBatchKey
        {
            OriginKind = AgentMemorySecurityArtifactBatchOriginKind.TrustedHostOperation,
            ArtifactPurpose = hostBatchKey.ArtifactPurpose,
            PreparationOrdinal = 0,
            ArtifactPlanHash = hostBatchKey.OperationFingerprint
        };
        var issued = await _store.TryIssueBatchAsync(batch, [handle], scope.MaxActiveResourceHandlesPerResource, scope.MaxResourceHandlesPerInvocation, cancellationToken).ConfigureAwait(false);
        return issued.Handles[0].HandleId;
    }

}
