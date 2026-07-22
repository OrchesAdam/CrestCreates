using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Tools.Adapters;

/// <summary>
/// Adapter: old IAgentMemoryResourceHandleStore → new IAgentMemoryAccessHandleStore.
/// Only AgentTool artifacts are visible. MCP artifacts return null.
/// </summary>
internal sealed class AgentMemoryResourceHandleStoreAdapter : IAgentMemoryResourceHandleStore
{
    private readonly IAgentMemoryAccessHandleStore _canonical;

    public AgentMemoryResourceHandleStoreAdapter(IAgentMemoryAccessHandleStore canonical)
    {
        _canonical = canonical;
    }

    public async ValueTask<AgentMemoryResourceHandle?> GetAsync(string handleId,
        CancellationToken cancellationToken = default)
    {
        var artifact = await _canonical.GetAsync(handleId, cancellationToken);
        if (artifact is null || artifact.Principal.CallerKind != AgentMemoryCallerKind.AgentTool)
            return null;
        return ConvertToOld(artifact);
    }

    public async ValueTask RevokeAsync(string handleId,
        CancellationToken cancellationToken = default)
    {
        var artifact = await _canonical.GetAsync(handleId, cancellationToken);
        if (artifact is null || artifact.Principal.CallerKind != AgentMemoryCallerKind.AgentTool)
            return;
        await _canonical.RevokeAsync(handleId, AgentMemoryCallerKind.AgentTool, cancellationToken);
    }

    public ValueTask<AgentMemoryResourceHandleIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        int maxActiveHandlesPerResource,
        CancellationToken cancellationToken = default)
        => TryIssueBatchAsync(batchKey, handles, maxActiveHandlesPerResource,
            int.MaxValue, cancellationToken);

    public async ValueTask<AgentMemoryResourceHandleIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        int maxActiveHandlesPerResource,
        int maxActiveHandlesPerInvocation,
        CancellationToken cancellationToken)
    {
        var newBatchKey = ConvertBatchKey(batchKey);
        var newHandles = handles.Select(ConvertToNew).ToList();

        var result = await _canonical.TryIssueBatchAsync(
            newBatchKey, newHandles,
            maxActiveHandlesPerResource, maxActiveHandlesPerInvocation,
            cancellationToken);

        return new AgentMemoryResourceHandleIssueResult
        {
            Handles = result.Handles.Select(ConvertToOld).ToList(),
            ReusedExisting = result.ReusedExisting,
        };
    }

    private static AgentMemoryAccessArtifactBatchKey ConvertBatchKey(
        AgentMemorySecurityArtifactBatchKey old)
        => new()
        {
            OriginKind = (AgentMemoryArtifactOriginKind)(int)old.OriginKind,
            OriginBindingHash = old.OriginBindingHash,
            ArtifactPurpose = old.ArtifactPurpose,
            PreparationOrdinal = old.PreparationOrdinal,
            ArtifactPlanHash = old.ArtifactPlanHash,
        };

    private static AgentMemoryResourceHandle ConvertToOld(AgentMemoryAccessResourceHandle a)
        => new()
        {
            HandleId = a.HandleId,
            ResourceKind = a.ResourceKind,
            ResourceId = a.ResourceId,
            Principal = new AgentMemoryToolPrincipal
            {
                TenantId = a.Principal.TenantId,
                UserId = a.Principal.UserId,
                AgentId = a.Principal.CallerId,
                ExecutionId = a.Principal.SecurityContextId,
            },
            ScopeFingerprint = a.ScopeFingerprint,
            RequiredDescriptorRefs = a.RequiredDescriptorRefs,
            IsUnscoped = a.IsUnscoped,
            IssuingInvocationId = a.IssuingOperationId,
            IssuedAt = a.IssuedAt,
            ExpiresAt = a.ExpiresAt,
            State = a.State,
        };

    private static AgentMemoryAccessResourceHandle ConvertToNew(AgentMemoryResourceHandle old)
        => new()
        {
            HandleId = old.HandleId,
            ResourceKind = old.ResourceKind,
            ResourceId = old.ResourceId,
            Principal = new AgentMemoryAccessPrincipal
            {
                TenantId = old.Principal.TenantId,
                UserId = old.Principal.UserId,
                CallerKind = AgentMemoryCallerKind.AgentTool,
                CallerId = old.Principal.AgentId,
                SecurityContextId = old.Principal.ExecutionId,
            },
            ScopeFingerprint = old.ScopeFingerprint,
            RequiredDescriptorRefs = old.RequiredDescriptorRefs,
            IsUnscoped = old.IsUnscoped,
            IssuingOperationId = old.IssuingInvocationId,
            IssuedAt = old.IssuedAt,
            ExpiresAt = old.ExpiresAt,
            State = old.State,
        };
}
