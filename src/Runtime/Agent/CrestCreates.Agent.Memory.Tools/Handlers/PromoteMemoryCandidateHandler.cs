using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Identity;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

[CapabilityName(AgentMemoryToolCapabilityIds.PromoteCandidate)]
internal sealed class PromoteMemoryCandidateHandler : AgentMemoryToolHandlerBase, ICapabilityHandler<PromoteMemoryCandidateInput, PromoteMemoryCandidateResult>
{
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemoryResourceHandleStore _handles;
    private readonly IAgentMemorySourceGrantStore _grants;
    private readonly IAgentMemoryStore _store;
    private readonly IAgentMemoryPromotionService _promotion;
    private readonly IAgentMemoryArtifactIdGenerator _ids;
    private readonly AgentMemoryCanonicalHashProjector _hashes;
    private readonly TimeProvider _time;

    public PromoteMemoryCandidateHandler(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemoryResourceHandleStore handles,
        IAgentMemorySourceGrantStore grants,
        IAgentMemoryStore store,
        IAgentMemoryPromotionService promotion,
        IAgentMemoryArtifactIdGenerator ids,
        AgentMemoryCanonicalHashProjector hashes,
        TimeProvider time)
        : base(capabilityContext, agentExecution)
    {
        _scopeProvider = scopeProvider; _handles = handles; _grants = grants; _store = store; _promotion = promotion; _ids = ids; _hashes = hashes; _time = time;
    }

    public async Task<PromoteMemoryCandidateResult> ExecuteAsync(PromoteMemoryCandidateInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope)) return Unavailable("scope-invalid");
        var handle = await _handles.GetAsync(input.CandidateHandle, ct).ConfigureAwait(false);
        if (!Usable(handle, principal, AgentMemoryResourceKind.Candidate)) return Unavailable("candidate-unavailable");
        var candidate = await _store.GetCandidateAsync(principal.TenantId, handle!.ResourceId, ct).ConfigureAwait(false);
        if (candidate is null) return Unavailable("candidate-unavailable");
        if (candidate.Status != AgentMemoryStatus.Candidate) return Conflict("candidate-consumed");
        var newMemoryId = _ids.CreateMemoryId();
        var now = _time.GetUtcNow();
        var itemHandle = new AgentMemoryResourceHandle
        {
            HandleId = AgentMemorySecurityArtifactIdGenerator.Create("hnd"), ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = newMemoryId, Principal = principal, ScopeFingerprint = handle.ScopeFingerprint,
            RequiredDescriptorRefs = candidate.DescriptorRefs, IsUnscoped = candidate.DescriptorRefs.Count == 0,
            IssuingInvocationId = principal.ExecutionId, IssuedAt = now, ExpiresAt = now.Add(scope.ResourceHandleLifetime)
        };
        var planHash = ArtifactPlanHash(principal, scope, "promote-memory", AgentMemoryResourceKind.Memory,
            newMemoryId, candidate.DescriptorRefs, candidate.SourceRefs, candidate.DescriptorRefs.Count == 0, scope.ResourceHandleLifetime);
        var grantsInput = candidate.SourceRefs.Select(source => new AgentMemorySourceGrant
        {
            GrantId = AgentMemorySecurityArtifactIdGenerator.Create("grt"), SourceRef = source, Principal = principal,
            ScopeFingerprint = handle.ScopeFingerprint, RequiredDescriptorRefs = candidate.DescriptorRefs.Concat(source.DescriptorRefs).Distinct().ToArray(),
            IsUnscoped = candidate.DescriptorRefs.Count == 0 && source.DescriptorRefs.Count == 0,
            IssuingInvocationId = principal.ExecutionId, IssuedAt = now, ExpiresAt = now.Add(scope.ExpansionGrantLifetime)
        }).ToArray();
        AgentMemoryResourceHandleIssueResult? preparedHandle = null;
        AgentMemoryGrantIssueResult? preparedGrantResult = null;
        try
        {
            preparedHandle = await _handles.TryIssueBatchAsync(AgentToolBatchKey(Context, "promote-memory-handle", planHash), [itemHandle], scope.MaxActiveResourceHandlesPerResource, scope.MaxResourceHandlesPerInvocation, ct).ConfigureAwait(false);
            preparedGrantResult = grantsInput.Length == 0 ? null
                : await _grants.TryIssueBatchAsync(AgentToolBatchKey(Context, "promote-memory-grants", planHash), grantsInput, scope.MaxGrantsPerResource, scope.MaxGrantsPerInvocation, ct).ConfigureAwait(false);
        }
        catch
        {
            await RevokeCreatedArtifactsAsync(_handles, preparedHandle, _grants, preparedGrantResult, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        var preparedGrants = preparedGrantResult?.Grants.ToArray() ?? Array.Empty<AgentMemorySourceGrant>();
        var completed = new PromoteMemoryCandidateResult
        {
            OperationStatus = AgentMemoryToolOperationStatus.Completed,
            Item = new AgentMemoryToolItemDto
            {
                MemoryHandle = preparedHandle!.Handles[0].HandleId, Kind = AgentMemoryToolProjection.ToToolKind(candidate.Kind),
                Content = candidate.Content, CanonicalContentHash = AgentMemoryToolProjection.ToToolHash(candidate.CanonicalContentHash),
                Confidence = AgentMemoryToolProjection.ToToolConfidence(candidate.Confidence),
                MemoryStatus = AgentMemoryToolMemoryStatus.Active, IsAuthoritative = false,
                Tags = candidate.Tags, SourceGrants = preparedGrants.Select(grant => new AgentMemorySourceGrantDto
                {
                    GrantId = grant.GrantId, SourceKind = AgentMemoryToolProjection.ToToolSourceKind(grant.SourceRef.SourceKind), ExpiresAt = grant.ExpiresAt
                }).ToArray()
            },
            Diagnostics = Array.Empty<AgentMemoryToolDiagnosticDto>()
        };
        var conflict = Conflict("candidate-transition-conflict");
        var unavailable = Unavailable("candidate-unavailable");
        AddBranchInvariantFacts(scope, "promote-memory-candidate");
        PublishAllowedOutcomes(
            ("completed", completed, AgentMemoryToolJsonSerializerContext.Default.PromoteMemoryCandidateResult),
            ("conflict", conflict, AgentMemoryToolJsonSerializerContext.Default.PromoteMemoryCandidateResult),
            ("unavailable", unavailable, AgentMemoryToolJsonSerializerContext.Default.PromoteMemoryCandidateResult));
        AgentMemoryItem memory;
        try
        {
            var request = AgentMemoryCurationHandlerHelpers.CreateRequest(principal, Execution, Context, "AgentToolPromotion", input.Explanation, now);
            var plannedMemory = new AgentMemoryItem
            {
                MemoryId = newMemoryId, TenantId = candidate.TenantId, Kind = candidate.Kind,
                Content = candidate.Content, CanonicalContentHash = candidate.CanonicalContentHash,
                PromotedAt = request.Timestamp, Confidence = candidate.Confidence,
                Status = AgentMemoryStatus.Active, IsAuthoritative = false, Tags = candidate.Tags,
                DescriptorRefs = candidate.DescriptorRefs, SourceRefs = candidate.SourceRefs,
                RedactionKinds = candidate.RedactionKinds, SanitizationDiagnostics = candidate.SanitizationDiagnostics
            };
            memory = await _promotion.PromoteAsync(principal.TenantId, new AgentMemoryPromotionPlan
            {
                Candidate = new AgentMemoryCandidateExpectation
                {
                    CandidateId = candidate.CandidateId,
                    ExpectedStateHash = _hashes.ComputeCandidateStateHash(candidate)
                },
                NewMemoryId = newMemoryId,
                ExpectedMemoryContentHash = candidate.CanonicalContentHash,
                ExpectedMemoryStateHash = _hashes.ComputeMemoryStateHash(plannedMemory),
                Operation = request
            }, ct).ConfigureAwait(false);
        }
        catch (AgentMemoryOperationException exception) when (exception.Code is
            AgentMemoryOperationFailureCode.ResourceUnavailable or
            AgentMemoryOperationFailureCode.InvalidLifecycleState or
            AgentMemoryOperationFailureCode.StateConflict or
            AgentMemoryOperationFailureCode.IdentityConflict)
        {
            await RevokeCreatedArtifactsAsync(_handles, preparedHandle, _grants, preparedGrantResult, CancellationToken.None).ConfigureAwait(false);
            // Resource disappearance is a confirmed zero-write unavailable
            // branch; lifecycle/expectation races are normal Conflict.
            return exception.Code == AgentMemoryOperationFailureCode.ResourceUnavailable
                ? Unavailable("candidate-unavailable")
                : conflict;
        }
        // The envelope was preflighted before the mutation. Do not construct a
        // second DTO after the domain call; verify that the committed graph is
        // exactly the graph represented by the prepared result and return that
        // immutable envelope unchanged.
        if (!string.Equals(memory.MemoryId, newMemoryId, StringComparison.Ordinal)
            || !string.Equals(memory.TenantId, principal.TenantId, StringComparison.Ordinal)
            || memory.Status != AgentMemoryStatus.Active
            || memory.CanonicalContentHash != candidate.CanonicalContentHash
            || !string.Equals(memory.Content, candidate.Content, StringComparison.Ordinal))
        {
            await RevokeCreatedArtifactsAsync(_handles, preparedHandle, _grants, preparedGrantResult, CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("Committed promotion graph differs from the preflight result.");
        }
        return completed;
    }

    private static bool Usable(AgentMemoryResourceHandle? handle, AgentMemoryToolPrincipal principal, AgentMemoryResourceKind kind)
        => handle is not null && handle.ResourceKind == kind && handle.Principal == principal
            && handle.State == AgentMemorySecurityArtifactState.Active && handle.ExpiresAt > DateTimeOffset.UtcNow;
    private static PromoteMemoryCandidateResult Unavailable(string code) => new() { OperationStatus = AgentMemoryToolOperationStatus.Unavailable, Item = null, Diagnostics = [Diagnostic(code)] };
    private static PromoteMemoryCandidateResult Conflict(string code) => new() { OperationStatus = AgentMemoryToolOperationStatus.Conflict, Item = null, Diagnostics = [Diagnostic(code)] };
}
