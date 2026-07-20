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
    private readonly IAgentMemorySourceGrantStore _grants;
    private readonly TimeProvider _time;

    public BuildAgentMemoryPackHandler(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemoryRetriever retriever,
        IAgentMemoryResourceHandleStore handles,
        IAgentMemorySourceGrantStore grants,
        TimeProvider time)
        : base(capabilityContext, agentExecution)
    {
        _scopeProvider = scopeProvider;
        _retriever = retriever;
        _handles = handles;
        _grants = grants;
        _time = time;
    }

    public async Task<BuildAgentMemoryPackResult> ExecuteAsync(BuildAgentMemoryPackInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope)) return Unavailable("scope-invalid");
        if (input.MaximumCount <= 0 || input.CharacterBudget <= 0
            || input.MaximumCount > scope.MaxRecallCount || input.CharacterBudget > scope.MaxRecallCharacters)
            return Unavailable("budget-invalid");

        var memoryIds = new List<string>();
        foreach (var handleId in input.MemoryHandles)
        {
            var handle = await _handles.GetAsync(handleId, ct).ConfigureAwait(false);
            if (!IsUsable(handle, principal, AgentMemoryResourceKind.Memory))
                return Unavailable("resource-unavailable");
            memoryIds.Add(handle!.ResourceId);
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
            return Unavailable("resource-unavailable");
        var visible = scope.VisibleDescriptorRefs.ToHashSet();
        if (scope.VisibleDescriptorRefs.Any(item => item.Version is not > 0)
            || pack.Memories.Any(memory => !IsVisible(memory, principal.TenantId, visible, scope.AllowUnscopedMemory)))
            return Unavailable("visibility-unavailable");

        var now = _time.GetUtcNow();
        var itemHandles = pack.Memories.Select(memory => new AgentMemoryResourceHandle
        {
            HandleId = AgentMemorySecurityArtifactIdGenerator.Create("hnd"),
            ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = memory.MemoryId,
            Principal = principal,
            ScopeFingerprint = ScopeFingerprint(scope, principal),
            RequiredDescriptorRefs = memory.DescriptorRefs,
            IsUnscoped = memory.DescriptorRefs.Count == 0,
            IssuingInvocationId = principal.ExecutionId,
            IssuedAt = now,
            ExpiresAt = now.Add(scope.ResourceHandleLifetime)
        }).ToArray();
        var handleBatch = AgentToolBatchKey(Context, "memory-pack-handles", PlanHash(itemHandles.Select(item => item.ResourceId), scope, principal, "memory-pack-handles"));
        var grantInputs = pack.Memories.SelectMany(memory => memory.SourceRefs.Select(sourceRef => new AgentMemorySourceGrant
        {
            GrantId = AgentMemorySecurityArtifactIdGenerator.Create("grt"),
            SourceRef = sourceRef,
            Principal = principal,
            ScopeFingerprint = ScopeFingerprint(scope, principal),
            RequiredDescriptorRefs = memory.DescriptorRefs.Concat(sourceRef.DescriptorRefs).Distinct().ToArray(),
            IsUnscoped = memory.DescriptorRefs.Count == 0 && sourceRef.DescriptorRefs.Count == 0,
            IssuingInvocationId = principal.ExecutionId,
            IssuedAt = now,
            ExpiresAt = now.Add(scope.ExpansionGrantLifetime)
        })).ToArray();
        AgentMemoryResourceHandleIssueResult? issuedHandles = null;
        AgentMemoryGrantIssueResult? issuedGrantResult = null;
        try
        {
            issuedHandles = await _handles.TryIssueBatchAsync(handleBatch, itemHandles, scope.MaxActiveResourceHandlesPerResource, scope.MaxResourceHandlesPerInvocation, ct).ConfigureAwait(false);
            issuedGrantResult = grantInputs.Length == 0
                ? null
                : await _grants.TryIssueBatchAsync(
                    AgentToolBatchKey(Context, "memory-pack-grants", PlanHash(grantInputs.Select(item => item.SourceRef.SourceId), scope, principal, "memory-pack-grants")),
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

        var handleByResource = issuedHandles!.Handles.ToDictionary(item => item.ResourceId, StringComparer.Ordinal);
        var issuedGrants = issuedGrantResult?.Grants.ToArray() ?? Array.Empty<AgentMemorySourceGrant>();
        var grantsBySource = issuedGrants.GroupBy(item => item.SourceRef.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(ToGrantDto).ToArray(), StringComparer.Ordinal);
        AddCommonFacts(scope, pack);
        return new BuildAgentMemoryPackResult
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
                SourceGrants = memory.SourceRefs.SelectMany(source => grantsBySource.TryGetValue(source.SourceId, out var grants) ? grants : Array.Empty<AgentMemorySourceGrantDto>()).ToArray()
            }).ToArray(),
            ReturnedCount = pack.Memories.Count,
            WasTruncated = pack.WasTruncated,
            IsAuthoritative = false,
            Diagnostics = Array.Empty<AgentMemoryToolDiagnosticDto>()
        };
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

    private static bool IsUsable(AgentMemoryResourceHandle? handle, AgentMemoryToolPrincipal principal, AgentMemoryResourceKind kind)
        => handle is not null
            && handle.ResourceKind == kind
            && handle.State == AgentMemorySecurityArtifactState.Active
            && handle.ExpiresAt > DateTimeOffset.UtcNow
            && handle.Principal == principal;

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

    private void AddCommonFacts(AgentMemoryToolAccessScope scope, AgentMemoryPack pack)
    {
        if (Context.Items.TryGetValue(AgentCapabilityContextItemNames.InvocationFactBuffer, out var value)
            && value is IAgentToolInvocationFactBuffer facts)
        {
            facts.AddTrustedFacts([
                new AgentToolAuditFact { Code = "memory.scope-fingerprint", Value = ScopeFingerprint(scope, Principal) },
                new AgentToolAuditFact { Code = "memory.pack-complete", Value = pack.WasTruncated ? "false" : "true" }
            ], scope.MaxAuditFacts);
        }
    }

    private static string ScopeFingerprint(AgentMemoryToolAccessScope scope, AgentMemoryToolPrincipal principal)
    {
        var payload = $"memory-scope-v2|{principal.TenantId}|{scope.AllowUnscopedMemory}|{string.Join('|', scope.VisibleDescriptorRefs
            .OrderBy(item => item.Namespace, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Version)
            .Select(item => $"{item.Namespace}:{item.Id}:{item.Version}"))}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string PlanHash(IEnumerable<string> values, AgentMemoryToolAccessScope scope, AgentMemoryToolPrincipal principal, string purpose)
    {
        var payload = $"memory-artifact-plan-v2|{principal.TenantId}|{principal.UserId}|{principal.AgentId}|{principal.ExecutionId}|{ScopeFingerprint(scope, principal)}|{purpose}|{string.Join('|', values.OrderBy(item => item, StringComparer.Ordinal))}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static AgentMemorySourceGrantDto ToGrantDto(AgentMemorySourceGrant grant) => new()
    {
        GrantId = grant.GrantId,
        SourceKind = AgentMemoryToolProjection.ToToolSourceKind(grant.SourceRef.SourceKind),
        ExpiresAt = grant.ExpiresAt
    };
}
