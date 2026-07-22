using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Tools.Adapters;

/// <summary>
/// Adapter: old IAgentMemorySourceGrantStore → new IAgentMemoryAccessGrantStore.
/// Only AgentTool artifacts are visible. MCP artifacts return null.
/// </summary>
internal sealed class AgentMemorySourceGrantStoreAdapter : IAgentMemorySourceGrantStore
{
    private readonly IAgentMemoryAccessGrantStore _canonical;

    public AgentMemorySourceGrantStoreAdapter(IAgentMemoryAccessGrantStore canonical)
    {
        _canonical = canonical;
    }

    public async ValueTask<AgentMemorySourceGrant?> GetAsync(string grantId,
        CancellationToken cancellationToken = default)
    {
        var artifact = await _canonical.GetAsync(grantId, cancellationToken);
        if (artifact is null || artifact.Principal.CallerKind != AgentMemoryCallerKind.AgentTool)
            return null;
        return ConvertToOld(artifact);
    }

    public async ValueTask RevokeAsync(string grantId,
        CancellationToken cancellationToken = default)
    {
        var artifact = await _canonical.GetAsync(grantId, cancellationToken);
        if (artifact is null || artifact.Principal.CallerKind != AgentMemoryCallerKind.AgentTool)
            return;
        await _canonical.RevokeAsync(grantId, AgentMemoryCallerKind.AgentTool, cancellationToken);
    }

    public ValueTask<AgentMemoryGrantIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        int maxActiveGrantsPerResource,
        CancellationToken cancellationToken = default)
        => TryIssueBatchAsync(batchKey, grants, maxActiveGrantsPerResource,
            int.MaxValue, cancellationToken);

    public async ValueTask<AgentMemoryGrantIssueResult> TryIssueBatchAsync(
        AgentMemorySecurityArtifactBatchKey batchKey,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        int maxActiveGrantsPerResource,
        int maxActiveGrantsPerInvocation,
        CancellationToken cancellationToken)
    {
        var newBatchKey = new AgentMemoryAccessArtifactBatchKey
        {
            OriginKind = (AgentMemoryArtifactOriginKind)(int)batchKey.OriginKind,
            OriginBindingHash = batchKey.OriginBindingHash,
            ArtifactPurpose = batchKey.ArtifactPurpose,
            PreparationOrdinal = batchKey.PreparationOrdinal,
            ArtifactPlanHash = batchKey.ArtifactPlanHash,
        };
        var newGrants = grants.Select(ConvertToNew).ToList();

        var result = await _canonical.TryIssueBatchAsync(
            newBatchKey, newGrants,
            maxActiveGrantsPerResource, maxActiveGrantsPerInvocation,
            cancellationToken);

        return new AgentMemoryGrantIssueResult
        {
            Grants = result.Grants.Select(ConvertToOld).ToList(),
            ReusedExisting = result.ReusedExisting,
        };
    }

    private static AgentMemorySourceGrant ConvertToOld(AgentMemoryAccessSourceGrant a)
        => new()
        {
            GrantId = a.GrantId,
            SourceRef = a.SourceRef,
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

    private static AgentMemoryAccessSourceGrant ConvertToNew(AgentMemorySourceGrant old)
        => new()
        {
            GrantId = old.GrantId,
            SourceRef = old.SourceRef,
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
