using System.Security.Cryptography;
using System.Text;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

[CapabilityName(AgentMemoryToolCapabilityIds.BuildPack)]
internal sealed class BuildAgentMemoryPackHandler : AgentMemoryToolHandlerBase, ICapabilityHandler<BuildAgentMemoryPackInput, BuildAgentMemoryPackResult>
{
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemoryRetriever _retriever;
    private readonly IAgentMemoryResourceHandleStore _handles;
    private readonly IAgentMemoryResourceHandleResolver _handleResolver;
    private readonly IAgentMemorySourceGrantStore _grants;
    private readonly TimeProvider _time;

    public BuildAgentMemoryPackHandler(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemoryRetriever retriever,
        IAgentMemoryResourceHandleStore handles,
        IAgentMemoryResourceHandleResolver handleResolver,
        IAgentMemorySourceGrantStore grants,
        TimeProvider time)
        : base(capabilityContext, agentExecution)
    {
        _scopeProvider = scopeProvider;
        _retriever = retriever;
        _handles = handles;
        _handleResolver = handleResolver;
        _grants = grants;
        _time = time;
    }

    public async Task<BuildAgentMemoryPackResult> ExecuteAsync(BuildAgentMemoryPackInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope)) return PrepareOutcome(scope, "build-memory-pack", "unavailable", Unavailable("scope-invalid"), AgentMemoryToolJsonSerializerContext.Default.BuildAgentMemoryPackResult);
        if (input.MaximumCount <= 0 || input.CharacterBudget <= 0
            || input.MaximumCount > scope.MaxRecallCount || input.CharacterBudget > scope.MaxRecallCharacters)
            return PrepareOutcome(scope, "build-memory-pack", "unavailable", Unavailable("budget-invalid"), AgentMemoryToolJsonSerializerContext.Default.BuildAgentMemoryPackResult);

        var memoryIds = new List<string>();
        foreach (var handleId in input.MemoryHandles)
        {
            var resolved = await _handleResolver.ResolveAsync(handleId, AgentMemoryResourceKind.Memory, principal, scope, ct).ConfigureAwait(false);
            if (resolved is null)
                return PrepareOutcome(scope, "build-memory-pack", "unavailable", Unavailable("resource-unavailable"), AgentMemoryToolJsonSerializerContext.Default.BuildAgentMemoryPackResult);
            memoryIds.Add(resolved.Handle.ResourceId);
        }

        var query = new AgentMemoryQuery
        {
            TenantId = principal.TenantId,
            VisibilityBoundary = new AgentMemoryVisibilityBoundary
            {
                VisibleDescriptorRefs = scope.VisibleDescriptorRefs,
                AllowUnscopedMemory = scope.AllowUnscopedMemory
            },
            MemoryIds = memoryIds,
            Kinds = input.Kinds.Select(ToDomainKind).ToArray(),
            Tags = input.Tags.ToArray(),
            VisibleDescriptorRefs = scope.VisibleDescriptorRefs,
            MaxCount = input.MaximumCount,
            CharacterBudget = input.CharacterBudget,
            MinimumConfidence = ToDomainConfidence(input.MinimumConfidence),
            IncludeSourceRefs = true
        };
        var pack = await _retriever.RecallAsync(query, ct).ConfigureAwait(false);
        if (!string.Equals(pack.TenantId, principal.TenantId, StringComparison.Ordinal))
            return PrepareOutcome(scope, "build-memory-pack", "unavailable", Unavailable("resource-unavailable"), AgentMemoryToolJsonSerializerContext.Default.BuildAgentMemoryPackResult);
        var visible = scope.VisibleDescriptorRefs.ToHashSet();
        if (scope.VisibleDescriptorRefs.Any(item => item.Version is not > 0)
            || pack.Memories.Any(memory => !IsVisible(memory, principal.TenantId, visible, scope.AllowUnscopedMemory)))
            return PrepareOutcome(scope, "build-memory-pack", "unavailable", Unavailable("visibility-unavailable"), AgentMemoryToolJsonSerializerContext.Default.BuildAgentMemoryPackResult);

        var now = _time.GetUtcNow();
        var itemHandles = pack.Memories.Select(memory => new AgentMemoryResourceHandle
        {
            HandleId = AgentMemorySecurityArtifactIdGenerator.Create("hnd"),
            ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = memory.MemoryId,
            Principal = principal,
            ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope, principal),
            RequiredDescriptorRefs = EffectiveDescriptorRefs(memory),
            IsUnscoped = EffectiveDescriptorRefs(memory).Count == 0,
            IssuingInvocationId = principal.ExecutionId,
            IssuedAt = now,
            ExpiresAt = now.Add(scope.ResourceHandleLifetime)
        }).ToArray();
        var grantInputs = pack.Memories.SelectMany(memory => memory.SourceRefs.Select(sourceRef => new AgentMemorySourceGrant
        {
            GrantId = AgentMemorySecurityArtifactIdGenerator.Create("grt"),
            SourceRef = sourceRef,
            Principal = principal,
            ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope, principal),
            RequiredDescriptorRefs = memory.DescriptorRefs.Concat(sourceRef.DescriptorRefs).Distinct().ToArray(),
            IsUnscoped = memory.DescriptorRefs.Count == 0 && sourceRef.DescriptorRefs.Count == 0,
            IssuingInvocationId = principal.ExecutionId,
            IssuedAt = now,
            ExpiresAt = now.Add(scope.ExpansionGrantLifetime)
        })).ToArray();
        var artifactPlanHash = AgentMemoryArtifactPlanProjector.Compute(principal, scope, "memory-pack", itemHandles, grantInputs);
        var handleBatch = AgentToolBatchKey(Context, "memory-pack-handles", artifactPlanHash);
        AgentMemoryResourceHandleIssueResult? issuedHandles = null;
        AgentMemoryGrantIssueResult? issuedGrantResult = null;
        try
        {
            issuedHandles = await _handles.TryIssueBatchAsync(handleBatch, itemHandles, scope.MaxActiveResourceHandlesPerResource, scope.MaxResourceHandlesPerInvocation, ct).ConfigureAwait(false);
            issuedGrantResult = grantInputs.Length == 0
                ? null
                : await _grants.TryIssueBatchAsync(
                    AgentToolBatchKey(Context, "memory-pack-grants", artifactPlanHash),
                    grantInputs,
                    scope.MaxGrantsPerResource,
                    scope.MaxGrantsPerInvocation,
                    ct).ConfigureAwait(false);
        }
        catch
        {
            await RevokeCreatedArtifactsAsync(_handles, issuedHandles, _grants, issuedGrantResult, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        try
        {
            if (issuedHandles is null || issuedHandles.Handles.Count != pack.Memories.Count)
                throw new InvalidOperationException("Memory pack handle preparation returned an invalid result.");
            var handleByResource = issuedHandles.Handles.ToDictionary(item => item.ResourceId, StringComparer.Ordinal);
            var issuedGrants = issuedGrantResult?.Grants.ToArray() ?? Array.Empty<AgentMemorySourceGrant>();
            var grantsBySource = issuedGrants.ToLookup(item => item.SourceRef, AgentContextSourceRefCanonicalComparer.Instance);
            AddBranchInvariantFacts(scope, "build-memory-pack");
            var result = new BuildAgentMemoryPackResult
            {
            OperationStatus = AgentMemoryToolOperationStatus.Completed,
            Items = pack.Memories.Select(memory => new AgentMemoryToolItemDto
            {
                MemoryHandle = handleByResource[memory.MemoryId].HandleId,
                Kind = AgentMemoryToolProjection.ToToolKind(memory.Kind),
                Content = memory.Content,
                CanonicalContentHash = AgentMemoryToolProjection.ToToolHash(memory.CanonicalContentHash),
                Confidence = AgentMemoryToolProjection.ToToolConfidence(memory.Confidence),
                MemoryStatus = AgentMemoryToolProjection.ToToolMemoryStatus(memory.Status),
                IsAuthoritative = false,
                Tags = memory.Tags,
                SourceGrants = memory.SourceRefs.SelectMany(source => grantsBySource[source].Select(ToGrantDto)).ToArray()
            }).ToArray(),
            ReturnedCount = pack.Memories.Count,
            WasTruncated = pack.WasTruncated,
            IsAuthoritative = false,
            Diagnostics = Array.Empty<AgentMemoryToolDiagnosticDto>()
            };
            PublishAllowedOutcomes(("completed", PrepareOutput(result, AgentMemoryToolJsonSerializerContext.Default.BuildAgentMemoryPackResult)));
            return result;
        }
        catch
        {
            await RevokeCreatedArtifactsAsync(_handles, issuedHandles, _grants, issuedGrantResult, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private BuildAgentMemoryPackResult Unavailable(string code) => new()
    {
        OperationStatus = AgentMemoryToolOperationStatus.Unavailable,
        Items = Array.Empty<AgentMemoryToolItemDto>(),
        ReturnedCount = 0,
        WasTruncated = false,
        IsAuthoritative = false,
        Diagnostics = [Diagnostic(code)]
    };

    private static IReadOnlyList<DescriptorRef> EffectiveDescriptorRefs(AgentMemoryItem memory)
        => memory.DescriptorRefs.Concat(memory.SourceRefs.SelectMany(item => item.DescriptorRefs)).Distinct().ToArray();

    private static bool IsVisible(
        AgentMemoryItem memory,
        string tenantId,
        IReadOnlySet<DescriptorRef> visible,
        bool allowUnscoped)
    {
        if (!string.Equals(memory.TenantId, tenantId, StringComparison.Ordinal))
            return false;
        if (memory.DescriptorRefs.Any(item => item.Version is not > 0)
            || memory.SourceRefs.Any(source => !string.Equals(source.TenantId, tenantId, StringComparison.Ordinal)
                || source.DescriptorRefs.Any(item => item.Version is not > 0)))
            return false;
        var required = memory.DescriptorRefs.Concat(memory.SourceRefs.SelectMany(source => source.DescriptorRefs)).ToArray();
        return required.Length == 0
            ? allowUnscoped
            : required.All(visible.Contains);
    }

    private static AgentMemoryKind ToDomainKind(AgentMemoryToolKind kind) => kind switch
    {
        AgentMemoryToolKind.Preference => AgentMemoryKind.Preference,
        AgentMemoryToolKind.ProjectFact => AgentMemoryKind.ProjectFact,
        AgentMemoryToolKind.Decision => AgentMemoryKind.Decision,
        AgentMemoryToolKind.Constraint => AgentMemoryKind.Constraint,
        AgentMemoryToolKind.WorkflowHint => AgentMemoryKind.WorkflowHint,
        AgentMemoryToolKind.Risk => AgentMemoryKind.Risk,
        _ => throw new InvalidOperationException("Unknown memory kind.")
    };

    private static AgentMemoryConfidence ToDomainConfidence(AgentMemoryToolConfidence confidence) => confidence switch
    {
        AgentMemoryToolConfidence.Unspecified => AgentMemoryConfidence.Unknown,
        AgentMemoryToolConfidence.Low => AgentMemoryConfidence.Low,
        AgentMemoryToolConfidence.Medium => AgentMemoryConfidence.Medium,
        AgentMemoryToolConfidence.High => AgentMemoryConfidence.High,
        _ => throw new InvalidOperationException("Unknown confidence.")
    };

    private static AgentMemorySourceGrantDto ToGrantDto(AgentMemorySourceGrant grant) => new()
    {
        GrantId = grant.GrantId,
        SourceKind = AgentMemoryToolProjection.ToToolSourceKind(grant.SourceRef.SourceKind),
        ExpiresAt = grant.ExpiresAt
    };
}
