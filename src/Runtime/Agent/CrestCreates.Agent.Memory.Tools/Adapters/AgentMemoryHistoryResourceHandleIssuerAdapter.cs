using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Tools.Adapters;

/// <summary>
/// Adapter: old IAgentMemoryHistoryResourceHandleIssuer → new IAgentMemoryContextHandleIssuer.
/// Maps HistorySourceKind → ResourceKind and passes sourceId as the authorized resource.
/// </summary>
internal sealed class AgentMemoryHistoryResourceHandleIssuerAdapter : IAgentMemoryHistoryResourceHandleIssuer
{
    private readonly IAgentMemoryContextHandleIssuer _canonical;

    public AgentMemoryHistoryResourceHandleIssuerAdapter(IAgentMemoryContextHandleIssuer canonical)
    {
        _canonical = canonical;
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

        var resourceKind = sourceKind switch
        {
            AgentMemoryHistorySourceKind.Conversation => AgentMemoryResourceKind.ConversationHistory,
            AgentMemoryHistorySourceKind.Task => AgentMemoryResourceKind.TaskHistory,
            _ => throw new InvalidOperationException("History source kind must not be Unknown.")
        };

        var result = await _canonical.IssueAsync(
            newPrincipal, origin,
            hostBatchKey.ArtifactPurpose ?? "history-handle",
            resourceKind,
            sourceId,
            cancellationToken);

        return result.HandleId;
    }
}
