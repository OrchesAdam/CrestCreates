using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Tools.Adapters;

/// <summary>
/// Adapter: implements new IAgentMemoryAccessScopeProvider by wrapping
/// old IAgentMemoryToolAccessScopeProvider. Only supports AgentTool callers.
/// </summary>
internal sealed class LegacyAgentMemoryAccessScopeProviderAdapter
    : IAgentMemoryAccessScopeProvider,
      IAgentMemoryAccessScopeProviderCapabilities
{
    private readonly IAgentMemoryToolAccessScopeProvider _legacy;

    public LegacyAgentMemoryAccessScopeProviderAdapter(
        IAgentMemoryToolAccessScopeProvider legacy)
    {
        _legacy = legacy;
    }

    public bool Supports(AgentMemoryCallerKind callerKind)
        => callerKind == AgentMemoryCallerKind.AgentTool;

    public async ValueTask<AgentMemoryAccessScope> ResolveAsync(
        AgentMemoryAccessPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var oldPrincipal = new AgentMemoryToolPrincipal
        {
            TenantId = principal.TenantId,
            UserId = principal.UserId,
            AgentId = principal.CallerId,
            ExecutionId = principal.SecurityContextId,
        };

        var oldScope = await _legacy.ResolveAsync(oldPrincipal, cancellationToken);

        return new AgentMemoryAccessScope
        {
            TenantId = principal.TenantId,
            VisibleDescriptorRefs = oldScope.VisibleDescriptorRefs,
            AllowUnscopedMemory = oldScope.AllowUnscopedMemory,
            MaxVisibleDescriptorRefs = oldScope.MaxVisibleDescriptorRefs,
            MaxRecallCount = oldScope.MaxRecallCount,
            MaxRecallCharacters = oldScope.MaxRecallCharacters,
            MaxExpansionCharacters = oldScope.MaxExpansionCharacters,
            MaxContextRecallCharacters = oldScope.MaxRecallCharacters, // Legacy mapping
            MaxCompressedBlockCount = oldScope.MaxCompressedBlockCount,
            MaxCompressedBlockCharacters = oldScope.MaxCompressedBlockCharacters,
            MaxCandidateCount = oldScope.MaxCandidateCount,
            MaxCandidateCharacters = oldScope.MaxCandidateCharacters,
            MaxSourceRefsPerArtifact = oldScope.MaxSourceRefsPerArtifact,
            MaxGrantsPerResource = oldScope.MaxGrantsPerResource,
            MaxGrantsPerOperation = oldScope.MaxGrantsPerInvocation,
            MaxResourceHandlesPerOperation = oldScope.MaxResourceHandlesPerInvocation,
            MaxActiveResourceHandlesPerResource = oldScope.MaxActiveResourceHandlesPerResource,
            MaxAuditFacts = oldScope.MaxAuditFacts,
            MaxTagsPerResource = oldScope.MaxTagsPerResource,
            ExpansionGrantLifetime = oldScope.ExpansionGrantLifetime,
            ResourceHandleLifetime = oldScope.ResourceHandleLifetime,
        };
    }
}
