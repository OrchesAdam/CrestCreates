using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Tools.Adapters;

/// <summary>
/// Adapter: old IAgentMemoryResourceHandleResolver → new IAgentMemoryAccessHandleResolver.
/// Only AgentTool artifacts are visible. MCP artifacts return null.
/// Also resolves the resource from domain stores since the new resolver only returns handles.
/// </summary>
internal sealed class AgentMemoryResourceHandleResolverAdapter : IAgentMemoryResourceHandleResolver
{
    private readonly IAgentMemoryAccessHandleResolver _canonical;
    private readonly IAgentMemoryStore _memoryStore;
    private readonly IAgentCompressedContextStore _contextStore;

    public AgentMemoryResourceHandleResolverAdapter(
        IAgentMemoryAccessHandleResolver canonical,
        IAgentMemoryStore memoryStore,
        IAgentCompressedContextStore contextStore)
    {
        _canonical = canonical;
        _memoryStore = memoryStore;
        _contextStore = contextStore;
    }

    public async ValueTask<AgentMemoryResolvedResourceHandle?> ResolveAsync(
        string handleId,
        AgentMemoryResourceKind expectedKind,
        AgentMemoryToolPrincipal principal,
        AgentMemoryToolAccessScope scope,
        CancellationToken cancellationToken = default)
    {
        var newPrincipal = ConvertPrincipal(principal);
        var newScope = ConvertScope(scope, principal.TenantId);

        var result = await _canonical.ResolveAsync(
            handleId, expectedKind, newPrincipal, newScope, cancellationToken);

        if (result is null) return null;
        if (result.Handle.Principal.CallerKind != AgentMemoryCallerKind.AgentTool)
            return null;

        // Resolve the actual resource from domain stores
        object? resource = await ResolveResourceAsync(expectedKind, principal.TenantId, result.Handle.ResourceId, cancellationToken);

        return new AgentMemoryResolvedResourceHandle
        {
            Handle = ConvertHandleToOld(result.Handle),
            Resource = resource,
            EffectiveDescriptorRefs = result.EffectiveDescriptorRefs,
        };
    }

    private async ValueTask<object?> ResolveResourceAsync(
        AgentMemoryResourceKind kind,
        string tenantId,
        string resourceId,
        CancellationToken cancellationToken)
    {
        return kind switch
        {
            AgentMemoryResourceKind.Memory =>
                await _memoryStore.GetMemoryAsync(tenantId, resourceId, cancellationToken).ConfigureAwait(false),
            AgentMemoryResourceKind.Candidate =>
                await _memoryStore.GetCandidateAsync(tenantId, resourceId, cancellationToken).ConfigureAwait(false),
            AgentMemoryResourceKind.Context =>
                await _contextStore.GetCompressedContextAsync(tenantId, resourceId, cancellationToken).ConfigureAwait(false),
            _ => null
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
}
