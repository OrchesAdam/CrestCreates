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
    private readonly IAgentMemoryResourceHandleResolver _handleResolver;
    private readonly IAgentMemorySourceGrantStore _grants;
    private readonly IAgentMemoryPromotionService _promotion;
    private readonly IAgentMemoryArtifactIdGenerator _ids;
    private readonly AgentMemoryCanonicalHashProjector _hashes;
    private readonly TimeProvider _time;

    public PromoteMemoryCandidateHandler(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemoryResourceHandleStore handles,
        IAgentMemoryResourceHandleResolver handleResolver,
        IAgentMemorySourceGrantStore grants,
        AgentMemoryToolRuntimeBinding runtimeBinding,
        IAgentMemoryArtifactIdGenerator ids,
        AgentMemoryCanonicalHashProjector hashes,
        TimeProvider time)
        : base(capabilityContext, agentExecution)
    {
        _scopeProvider = scopeProvider; _handles = handles; _handleResolver = handleResolver; _grants = grants; _promotion = runtimeBinding.PromotionService; _ids = ids; _hashes = hashes; _time = time;
    }

    public async Task<PromoteMemoryCandidateResult> ExecuteAsync(PromoteMemoryCandidateInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope)) return PrepareOutcome(scope, "promote-memory-candidate", "unavailable", Unavailable("scope-invalid"), AgentMemoryToolJsonSerializerContext.Default.PromoteMemoryCandidateResult);
        var resolved = await _handleResolver.ResolveAsync(input.CandidateHandle, AgentMemoryResourceKind.Candidate, principal, scope, ct).ConfigureAwait(false);
        if (resolved?.Resource is not AgentMemoryCandidate candidate) return PrepareOutcome(scope, "promote-memory-candidate", "unavailable", Unavailable("candidate-unavailable"), AgentMemoryToolJsonSerializerContext.Default.PromoteMemoryCandidateResult);
        var handle = resolved.Handle;
        if (candidate.Status != AgentMemoryStatus.Candidate) return PrepareOutcome(scope, "promote-memory-candidate", "conflict", Conflict("candidate-consumed"), AgentMemoryToolJsonSerializerContext.Default.PromoteMemoryCandidateResult);
        var newMemoryId = _ids.CreateMemoryId();
        var now = _time.GetUtcNow();
        var itemHandle = new AgentMemoryResourceHandle
        {
            HandleId = AgentMemorySecurityArtifactIdGenerator.Create("hnd"), ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = newMemoryId, Principal = principal, ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope, principal),
            RequiredDescriptorRefs = EffectiveDescriptorRefs(candidate), IsUnscoped = EffectiveDescriptorRefs(candidate).Count == 0,
            IssuingInvocationId = principal.ExecutionId, IssuedAt = now, ExpiresAt = now.Add(scope.ResourceHandleLifetime)
        };
        var grantsInput = candidate.SourceRefs.Select(source => new AgentMemorySourceGrant
        {
            GrantId = AgentMemorySecurityArtifactIdGenerator.Create("grt"), SourceRef = source, Principal = principal,
            ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope, principal), RequiredDescriptorRefs = EffectiveDescriptorRefs(candidate).Concat(source.DescriptorRefs).Distinct().ToArray(),
            IsUnscoped = EffectiveDescriptorRefs(candidate).Count == 0 && source.DescriptorRefs.Count == 0,
            IssuingInvocationId = principal.ExecutionId, IssuedAt = now, ExpiresAt = now.Add(scope.ExpansionGrantLifetime)
        }).ToArray();
        var planHash = AgentMemoryArtifactPlanProjector.Compute(principal, scope, "promote-memory", [itemHandle], grantsInput);
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
        try
        {
        var preparedGrants = preparedGrantResult?.Grants.ToArray() ?? Array.Empty<AgentMemorySourceGrant>();
        if (preparedHandle is null || preparedHandle.Handles.Count != 1)
            throw new InvalidOperationException("Promotion handle preparation returned an invalid result.");
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
            ("completed", PrepareOutput(completed, AgentMemoryToolJsonSerializerContext.Default.PromoteMemoryCandidateResult)),
            ("conflict", PrepareOutput(conflict, AgentMemoryToolJsonSerializerContext.Default.PromoteMemoryCandidateResult)),
            ("unavailable", PrepareOutput(unavailable, AgentMemoryToolJsonSerializerContext.Default.PromoteMemoryCandidateResult)));
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
            throw new AgentMemoryPostCommitIntegrityException("Committed promotion graph differs from the preflight result.");
        }
        return completed;
        }
        catch
        {
            await RevokeCreatedArtifactsAsync(_handles, preparedHandle, _grants, preparedGrantResult, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static IReadOnlyList<Metadata.Abstractions.DescriptorRef> EffectiveDescriptorRefs(AgentMemoryCandidate candidate)
        => candidate.DescriptorRefs.Concat(candidate.SourceRefs.SelectMany(item => item.DescriptorRefs)).Distinct().ToArray();
    private static PromoteMemoryCandidateResult Unavailable(string code) => new() { OperationStatus = AgentMemoryToolOperationStatus.Unavailable, Item = null, Diagnostics = [Diagnostic(code)] };
    private static PromoteMemoryCandidateResult Conflict(string code) => new() { OperationStatus = AgentMemoryToolOperationStatus.Conflict, Item = null, Diagnostics = [Diagnostic(code)] };
}
