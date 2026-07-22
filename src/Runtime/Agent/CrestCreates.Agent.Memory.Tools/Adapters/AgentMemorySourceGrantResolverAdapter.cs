using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Tools.Adapters;

/// <summary>
/// Adapter: old IAgentMemorySourceGrantResolver → new IAgentMemoryAccessGrantResolver.
/// Only AgentTool artifacts are visible. MCP artifacts return null.
/// </summary>
internal sealed class AgentMemorySourceGrantResolverAdapter : IAgentMemorySourceGrantResolver
{
    private readonly IAgentMemoryAccessGrantResolver _canonical;

    public AgentMemorySourceGrantResolverAdapter(IAgentMemoryAccessGrantResolver canonical)
    {
        _canonical = canonical;
    }

    public async ValueTask<AgentMemorySourceGrant?> ResolveAsync(
        string grantId,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        CancellationToken cancellationToken = default)
    {
        var newPrincipal = new AgentMemoryAccessPrincipal
        {
            TenantId = principal.TenantId,
            UserId = principal.UserId,
            CallerKind = AgentMemoryCallerKind.AgentTool,
            CallerId = principal.AgentId,
            SecurityContextId = principal.ExecutionId,
        };

        var newScope = new AgentMemoryAccessScope
        {
            TenantId = principal.TenantId,
            VisibleDescriptorRefs = scope.VisibleDescriptorRefs,
            AllowUnscopedMemory = scope.AllowUnscopedMemory,
            MaxVisibleDescriptorRefs = scope.MaxVisibleDescriptorRefs,
            MaxRecallCount = scope.MaxRecallCount,
            MaxRecallCharacters = scope.MaxRecallCharacters,
            MaxExpansionCharacters = scope.MaxExpansionCharacters,
            MaxContextRecallCharacters = scope.MaxRecallCharacters,
            MaxCompressedBlockCount = scope.MaxCompressedBlockCount,
            MaxCompressedBlockCharacters = scope.MaxCompressedBlockCharacters,
            MaxCandidateCount = scope.MaxCandidateCount,
            MaxCandidateCharacters = scope.MaxCandidateCharacters,
            MaxSourceRefsPerArtifact = scope.MaxSourceRefsPerArtifact,
            MaxGrantsPerResource = scope.MaxGrantsPerResource,
            MaxGrantsPerOperation = scope.MaxGrantsPerInvocation,
            MaxResourceHandlesPerOperation = scope.MaxResourceHandlesPerInvocation,
            MaxActiveResourceHandlesPerResource = scope.MaxActiveResourceHandlesPerResource,
            MaxAuditFacts = scope.MaxAuditFacts,
            MaxTagsPerResource = scope.MaxTagsPerResource,
            ExpansionGrantLifetime = scope.ExpansionGrantLifetime,
            ResourceHandleLifetime = scope.ResourceHandleLifetime,
        };

        var result = await _canonical.ResolveAsync(
            grantId, newPrincipal, newScope, cancellationToken);

        if (result is null) return null;
        if (result.Principal.CallerKind != AgentMemoryCallerKind.AgentTool)
            return null;

        return new AgentMemorySourceGrant
        {
            GrantId = result.GrantId,
            SourceRef = result.SourceRef,
            Principal = principal, // Already matches
            ScopeFingerprint = result.ScopeFingerprint,
            RequiredDescriptorRefs = result.RequiredDescriptorRefs,
            IsUnscoped = result.IsUnscoped,
            IssuingInvocationId = result.IssuingOperationId,
            IssuedAt = result.IssuedAt,
            ExpiresAt = result.ExpiresAt,
            State = result.State,
        };
    }
}
