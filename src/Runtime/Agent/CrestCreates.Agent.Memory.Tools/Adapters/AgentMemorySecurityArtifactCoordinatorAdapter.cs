using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Agent.Tools;

namespace CrestCreates.Agent.Memory.Tools.Adapters;

/// <summary>
/// Adapter: old IAgentMemorySecurityArtifactCoordinator → new IAgentMemoryAccessArtifactCoordinator.
/// Converts old ToolPrincipal/ToolAccessScope patterns to new projection-neutral types.
/// Embeds compensation token in the returned prepared object — no AsyncLocal.
/// </summary>
internal sealed class AgentMemorySecurityArtifactCoordinatorAdapter : IAgentMemorySecurityArtifactCoordinator
{
    private readonly IAgentMemoryAccessArtifactCoordinator _canonical;

    public AgentMemorySecurityArtifactCoordinatorAdapter(IAgentMemoryAccessArtifactCoordinator canonical)
    {
        _canonical = canonical;
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
        var newPrincipal = ConvertPrincipal(principal);
        var origin = new AgentMemoryArtifactOrigin
        {
            Kind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OperationId = binding.LogicalKey.InvocationId,
            BindingHash = CanonicalHashing.CreateOriginBindingHash(binding),
        };

        var newScope = ConvertScope(scope, principal.TenantId);
        var newHandles = handles.Select(ConvertHandleToNew).ToList();
        var newGrants = grants.Select(ConvertGrantToNew).ToList();

        var result = await _canonical.PrepareAsync(
            newPrincipal, origin, newScope, artifactPurpose,
            preparationOrdinal, newHandles, newGrants, cancellationToken);

        return new AgentMemoryPreparedSecurityArtifacts
        {
            Handles = result.Handles is not null
                ? new AgentMemoryResourceHandleIssueResult
                {
                    Handles = result.Handles.Handles.Select(ConvertHandleToOld).ToList(),
                    ReusedExisting = result.Handles.ReusedExisting,
                }
                : null,
            Grants = result.Grants is not null
                ? new AgentMemoryGrantIssueResult
                {
                    Grants = result.Grants.Grants.Select(ConvertGrantToOld).ToList(),
                    ReusedExisting = result.Grants.ReusedExisting,
                }
                : null,
            BridgedCompensationToken = result.CompensationToken,
        };
    }

    public async ValueTask RevokeCreatedAsync(
        AgentMemoryPreparedSecurityArtifacts prepared,
        CancellationToken cancellationToken = default)
    {
        if (prepared.BridgedCompensationToken is not null)
        {
            await _canonical.RevokeCreatedAsync(prepared.BridgedCompensationToken, cancellationToken);
        }
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
        var newPrincipal = ConvertPrincipal(principal);
        var origin = new AgentMemoryArtifactOrigin
        {
            Kind = AgentMemoryArtifactOriginKind.TrustedHostOperation,
            OperationId = hostBatchKey.HostOperationId,
            BindingHash = hostBatchKey.OperationFingerprint,
        };

        var newScope = ConvertScope(scope, principal.TenantId);
        var newHandles = handles.Select(ConvertHandleToNew).ToList();
        var newGrants = grants.Select(ConvertGrantToNew).ToList();

        var result = await _canonical.PrepareAsync(
            newPrincipal, origin, newScope, hostBatchKey.ArtifactPurpose,
            preparationOrdinal: 0, newHandles, newGrants, cancellationToken);

        return new AgentMemoryPreparedSecurityArtifacts
        {
            Handles = result.Handles is not null
                ? new AgentMemoryResourceHandleIssueResult
                {
                    Handles = result.Handles.Handles.Select(ConvertHandleToOld).ToList(),
                    ReusedExisting = result.Handles.ReusedExisting,
                }
                : null,
            Grants = result.Grants is not null
                ? new AgentMemoryGrantIssueResult
                {
                    Grants = result.Grants.Grants.Select(ConvertGrantToOld).ToList(),
                    ReusedExisting = result.Grants.ReusedExisting,
                }
                : null,
            BridgedCompensationToken = result.CompensationToken,
        };
    }

    private static AgentMemoryAccessPrincipal ConvertPrincipal(AgentMemoryToolPrincipal p)
        => new()
        {
            TenantId = p.TenantId,
            UserId = p.UserId,
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = p.AgentId,
            SecurityContextId = p.ExecutionId,
        };

    private static AgentMemoryAccessScope ConvertScope(AgentMemoryToolAccessScope s, string tenantId)
        => new()
        {
            TenantId = tenantId,
            VisibleDescriptorRefs = s.VisibleDescriptorRefs,
            AllowUnscopedMemory = s.AllowUnscopedMemory,
            MaxVisibleDescriptorRefs = s.MaxVisibleDescriptorRefs,
            MaxRecallCount = s.MaxRecallCount,
            MaxRecallCharacters = s.MaxRecallCharacters,
            MaxExpansionCharacters = s.MaxExpansionCharacters,
            MaxContextRecallCharacters = s.MaxRecallCharacters,
            MaxCompressedBlockCount = s.MaxCompressedBlockCount,
            MaxCompressedBlockCharacters = s.MaxCompressedBlockCharacters,
            MaxCandidateCount = s.MaxCandidateCount,
            MaxCandidateCharacters = s.MaxCandidateCharacters,
            MaxSourceRefsPerArtifact = s.MaxSourceRefsPerArtifact,
            MaxGrantsPerResource = s.MaxGrantsPerResource,
            MaxGrantsPerOperation = s.MaxGrantsPerInvocation,
            MaxResourceHandlesPerOperation = s.MaxResourceHandlesPerInvocation,
            MaxActiveResourceHandlesPerResource = s.MaxActiveResourceHandlesPerResource,
            MaxAuditFacts = s.MaxAuditFacts,
            MaxTagsPerResource = s.MaxTagsPerResource,
            ExpansionGrantLifetime = s.ExpansionGrantLifetime,
            ResourceHandleLifetime = s.ResourceHandleLifetime,
        };

    private static AgentMemoryAccessResourceHandle ConvertHandleToNew(AgentMemoryResourceHandle old)
        => new()
        {
            HandleId = old.HandleId,
            ResourceKind = old.ResourceKind,
            ResourceId = old.ResourceId,
            Principal = ConvertPrincipal(old.Principal),
            ScopeFingerprint = old.ScopeFingerprint,
            RequiredDescriptorRefs = old.RequiredDescriptorRefs,
            IsUnscoped = old.IsUnscoped,
            IssuingOperationId = old.IssuingInvocationId,
            IssuedAt = old.IssuedAt,
            ExpiresAt = old.ExpiresAt,
            State = old.State,
        };

    private static AgentMemoryResourceHandle ConvertHandleToOld(AgentMemoryAccessResourceHandle a)
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

    private static AgentMemoryAccessSourceGrant ConvertGrantToNew(AgentMemorySourceGrant old)
        => new()
        {
            GrantId = old.GrantId,
            SourceRef = old.SourceRef,
            Principal = ConvertPrincipal(old.Principal),
            ScopeFingerprint = old.ScopeFingerprint,
            RequiredDescriptorRefs = old.RequiredDescriptorRefs,
            IsUnscoped = old.IsUnscoped,
            IssuingOperationId = old.IssuingInvocationId,
            IssuedAt = old.IssuedAt,
            ExpiresAt = old.ExpiresAt,
            State = old.State,
        };

    private static AgentMemorySourceGrant ConvertGrantToOld(AgentMemoryAccessSourceGrant a)
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

    private static class CanonicalHashing
    {
        public static CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash CreateOriginBindingHash(
            AgentToolInvocationBindingSnapshot binding)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var raw = System.Text.Encoding.UTF8.GetBytes($"origin-{binding.LogicalKey.InvocationId}-{binding.InvocationFingerprint}");
            return new CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash
            {
                Value = Convert.ToHexString(sha256.ComputeHash(raw)).ToLowerInvariant(),
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "agent-memory-security-artifact-origin-binding",
                Scope = "TenantVisible",
                Purpose = "SourceBinding",
                ContractVersion = "memory-security-artifact-v2",
                CanonicalShapeVersion = "agent-tool-origin-binding-v3",
            };
        }
    }
}
