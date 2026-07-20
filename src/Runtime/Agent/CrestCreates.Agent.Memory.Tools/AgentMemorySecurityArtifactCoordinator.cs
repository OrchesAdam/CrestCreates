using System.Security.Cryptography;
using System.Text;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Tools;

internal sealed class AgentMemorySecurityArtifactCoordinator : IAgentMemorySecurityArtifactCoordinator
{
    private readonly IAgentMemoryResourceHandleStore _handles;
    private readonly IAgentMemorySourceGrantStore _grants;

    public AgentMemorySecurityArtifactCoordinator(
        IAgentMemoryResourceHandleStore handles,
        IAgentMemorySourceGrantStore grants)
    {
        _handles = handles;
        _grants = grants;
    }

    public async ValueTask<AgentMemoryPreparedSecurityArtifacts> PrepareForAgentToolAsync(
        AgentToolInvocationBindingSnapshot binding,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        string artifactPurpose,
        int preparationOrdinal,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(scope);
        var planHash = AgentMemoryArtifactPlanProjector.Compute(principal, scope, artifactPurpose, handles, grants);
        var originHash = CreateOriginBindingHash(binding);
        var handleResult = (AgentMemoryResourceHandleIssueResult?)null;
        var grantResult = (AgentMemoryGrantIssueResult?)null;
        try
        {
            if (handles.Count > 0)
                handleResult = await _handles.TryIssueBatchAsync(
                    new AgentMemorySecurityArtifactBatchKey
                    {
                        OriginKind = AgentMemorySecurityArtifactBatchOriginKind.AgentToolInvocation,
                        OriginBindingHash = originHash,
                        ArtifactPurpose = $"{artifactPurpose}-handles",
                        PreparationOrdinal = preparationOrdinal,
                        ArtifactPlanHash = planHash
                    }, handles, scope.MaxActiveResourceHandlesPerResource,
                    scope.MaxResourceHandlesPerInvocation, cancellationToken).ConfigureAwait(false);
            if (grants.Count > 0)
                grantResult = await _grants.TryIssueBatchAsync(
                    new AgentMemorySecurityArtifactBatchKey
                    {
                        OriginKind = AgentMemorySecurityArtifactBatchOriginKind.AgentToolInvocation,
                        OriginBindingHash = originHash,
                        ArtifactPurpose = $"{artifactPurpose}-grants",
                        PreparationOrdinal = preparationOrdinal,
                        ArtifactPlanHash = planHash
                    }, grants, scope.MaxGrantsPerResource,
                    scope.MaxGrantsPerInvocation, cancellationToken).ConfigureAwait(false);
            return new AgentMemoryPreparedSecurityArtifacts { Handles = handleResult, Grants = grantResult };
        }
        catch
        {
            await RevokeCreatedAsync(new AgentMemoryPreparedSecurityArtifacts { Handles = handleResult, Grants = grantResult }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask RevokeCreatedAsync(
        AgentMemoryPreparedSecurityArtifacts prepared,
        CancellationToken cancellationToken = default)
    {
        if (prepared.Handles is { ReusedExisting: false })
            foreach (var handle in prepared.Handles.Handles)
                await _handles.RevokeAsync(handle.HandleId, cancellationToken).ConfigureAwait(false);
        if (prepared.Grants is { ReusedExisting: false })
            foreach (var grant in prepared.Grants.Grants)
                await _grants.RevokeAsync(grant.GrantId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AgentMemoryPreparedSecurityArtifacts> PrepareForHostAsync(
        AgentMemoryHostArtifactBatchKey hostBatchKey,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        AgentMemoryHistorySourceKind sourceKind,
        string sourceId,
        IReadOnlyList<AgentMemoryResourceHandle> handles,
        IReadOnlyList<AgentMemorySourceGrant> grants,
        CancellationToken cancellationToken = default)
    {
        if (handles.Count == 0)
            throw new InvalidOperationException("A Host artifact preparation requires at least one resource handle.");
        var batch = AgentMemoryHostArtifactBatchProjector.Create(
            hostBatchKey, principal, scope, sourceKind, sourceId, handles, grants);
        AgentMemoryResourceHandleIssueResult? handleResult = null;
        AgentMemoryGrantIssueResult? grantResult = null;
        try
        {
            handleResult = await _handles.TryIssueBatchAsync(
                batch, handles, scope.MaxActiveResourceHandlesPerResource,
                scope.MaxResourceHandlesPerInvocation, cancellationToken).ConfigureAwait(false);
            if (grants.Count > 0)
                grantResult = await _grants.TryIssueBatchAsync(
                    batch with { ArtifactPurpose = $"{batch.ArtifactPurpose}-grants" }, grants,
                    scope.MaxGrantsPerResource, scope.MaxGrantsPerInvocation, cancellationToken).ConfigureAwait(false);
            return new AgentMemoryPreparedSecurityArtifacts { Handles = handleResult, Grants = grantResult };
        }
        catch
        {
            await RevokeCreatedAsync(new AgentMemoryPreparedSecurityArtifacts { Handles = handleResult, Grants = grantResult }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static CanonicalHash CreateOriginBindingHash(AgentToolInvocationBindingSnapshot binding)
    {
        var value = string.Join('|', "agent-tool-origin-binding-v2", binding.LogicalKey.TenantId,
            binding.LogicalKey.UserId, binding.LogicalKey.AgentId, binding.LogicalKey.ExecutionId,
            binding.LogicalKey.InvocationId, binding.InvocationFingerprint);
        return new CanonicalHash
        {
            Value = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant(),
            Algorithm = "SHA-256", AlgorithmVersion = "sha256-length-prefixed-v1",
            ArtifactKind = "agent-memory-security-artifact-origin-binding", Scope = "TenantVisible",
            Purpose = "SourceBinding", ContractVersion = "memory-security-artifact-v2",
            CanonicalShapeVersion = "agent-tool-origin-binding-v2"
        };
    }
}
