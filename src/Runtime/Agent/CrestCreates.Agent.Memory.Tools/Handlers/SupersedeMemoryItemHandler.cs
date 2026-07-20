using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

[CapabilityName(AgentMemoryToolCapabilityIds.SupersedeItem)]
internal sealed class SupersedeMemoryItemHandler : AgentMemoryToolHandlerBase, ICapabilityHandler<SupersedeMemoryItemInput, SupersedeMemoryItemResult>
{
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemoryResourceHandleStore _handles;
    private readonly IAgentMemoryResourceHandleResolver _handleResolver;
    private readonly IAgentMemorySourceGrantStore _grants;
    private readonly IAgentMemoryPromotionService _promotion;
    private readonly IAgentMemoryArtifactIdGenerator _ids;
    private readonly AgentMemoryCanonicalHashProjector _hashes;
    private readonly TimeProvider _time;

    public SupersedeMemoryItemHandler(
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
    { _scopeProvider = scopeProvider; _handles = handles; _handleResolver = handleResolver; _grants = grants; _promotion = runtimeBinding.PromotionService; _ids = ids; _hashes = hashes; _time = time; }

    public async Task<SupersedeMemoryItemResult> ExecuteAsync(SupersedeMemoryItemInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope)) return PrepareOutcome(scope, "supersede-memory-item", "unavailable", Unavailable("scope-invalid"), AgentMemoryToolJsonSerializerContext.Default.SupersedeMemoryItemResult);
        var targetResolved = await _handleResolver.ResolveAsync(input.MemoryHandle, AgentMemoryResourceKind.Memory, principal, scope, ct).ConfigureAwait(false);
        var replacementResolved = await _handleResolver.ResolveAsync(input.ReplacementCandidateHandle, AgentMemoryResourceKind.Candidate, principal, scope, ct).ConfigureAwait(false);
        if (targetResolved?.Resource is not AgentMemoryItem target || replacementResolved?.Resource is not AgentMemoryCandidate replacement)
            return PrepareOutcome(scope, "supersede-memory-item", "unavailable", Unavailable("resource-unavailable"), AgentMemoryToolJsonSerializerContext.Default.SupersedeMemoryItemResult);
        if (target.Status != AgentMemoryStatus.Active || replacement.Status != AgentMemoryStatus.Candidate) return PrepareOutcome(scope, "supersede-memory-item", "conflict", Conflict("lifecycle-conflict"), AgentMemoryToolJsonSerializerContext.Default.SupersedeMemoryItemResult);
        var newMemoryId = _ids.CreateMemoryId();
        var now = _time.GetUtcNow();
        var newHandle = new AgentMemoryResourceHandle
        {
            HandleId = AgentMemorySecurityArtifactIdGenerator.Create("hnd"), ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = newMemoryId, Principal = principal, ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope, principal),
            RequiredDescriptorRefs = EffectiveDescriptorRefs(replacement), IsUnscoped = EffectiveDescriptorRefs(replacement).Count == 0,
            IssuingInvocationId = principal.ExecutionId, IssuedAt = now, ExpiresAt = now.Add(scope.ResourceHandleLifetime)
        };
        AgentMemoryResourceHandleIssueResult? prepared = null;
        var grantInputs = replacement.SourceRefs.Select(source => new AgentMemorySourceGrant
        {
            GrantId = AgentMemorySecurityArtifactIdGenerator.Create("grt"), SourceRef = source, Principal = principal,
            ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope, principal), RequiredDescriptorRefs = EffectiveDescriptorRefs(replacement).Concat(source.DescriptorRefs).Distinct().ToArray(),
            IsUnscoped = EffectiveDescriptorRefs(replacement).Count == 0 && source.DescriptorRefs.Count == 0,
            IssuingInvocationId = principal.ExecutionId, IssuedAt = now, ExpiresAt = now.Add(scope.ExpansionGrantLifetime)
        }).ToArray();
        var planHash = AgentMemoryArtifactPlanProjector.Compute(principal, scope, "supersede-memory", [newHandle], grantInputs);
        AgentMemoryGrantIssueResult? grantResult = null;
        try
        {
            prepared = await _handles.TryIssueBatchAsync(AgentToolBatchKey(Context, "supersede-memory-handle", planHash), [newHandle], scope.MaxActiveResourceHandlesPerResource, scope.MaxResourceHandlesPerInvocation, ct).ConfigureAwait(false);
            grantResult = grantInputs.Length == 0 ? null
                : await _grants.TryIssueBatchAsync(AgentToolBatchKey(Context, "supersede-memory-grants", planHash), grantInputs, scope.MaxGrantsPerResource, scope.MaxGrantsPerInvocation, ct).ConfigureAwait(false);
        }
        catch
        {
            await RevokeCreatedArtifactsAsync(_handles, prepared, _grants, grantResult, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        if (prepared is null || prepared.Handles.Count != 1)
        {
            await RevokeCreatedArtifactsAsync(_handles, prepared, _grants, grantResult, CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("Supersession handle preparation returned an invalid result.");
        }
        try
        {
        var grants = grantResult?.Grants.ToArray() ?? Array.Empty<AgentMemorySourceGrant>();
        var completed = new SupersedeMemoryItemResult
        {
            OperationStatus = AgentMemoryToolOperationStatus.Completed,
            Item = new AgentMemoryToolItemDto
            {
                MemoryHandle = prepared!.Handles[0].HandleId, Kind = AgentMemoryToolProjection.ToToolKind(replacement.Kind), Content = replacement.Content,
                CanonicalContentHash = AgentMemoryToolProjection.ToToolHash(replacement.CanonicalContentHash), Confidence = AgentMemoryToolProjection.ToToolConfidence(replacement.Confidence),
                MemoryStatus = AgentMemoryToolMemoryStatus.Active, IsAuthoritative = false, Tags = replacement.Tags,
                SourceGrants = grants.Select(grant => new AgentMemorySourceGrantDto { GrantId = grant.GrantId, SourceKind = AgentMemoryToolProjection.ToToolSourceKind(grant.SourceRef.SourceKind), ExpiresAt = grant.ExpiresAt }).ToArray()
            },
            SupersededMemoryHandle = input.MemoryHandle, ActiveMemoryHandle = prepared!.Handles[0].HandleId,
            Diagnostics = Array.Empty<AgentMemoryToolDiagnosticDto>()
        };
        var conflict = Conflict("lifecycle-conflict");
        var unavailable = Unavailable("resource-unavailable");
        AddBranchInvariantFacts(scope, "supersede-memory-item");
        PublishAllowedOutcomes(
            ("completed", PrepareOutput(completed, AgentMemoryToolJsonSerializerContext.Default.SupersedeMemoryItemResult)),
            ("conflict", PrepareOutput(conflict, AgentMemoryToolJsonSerializerContext.Default.SupersedeMemoryItemResult)),
            ("unavailable", PrepareOutput(unavailable, AgentMemoryToolJsonSerializerContext.Default.SupersedeMemoryItemResult)));
        AgentMemoryItem memory;
        try
        {
            var request = AgentMemoryCurationHandlerHelpers.CreateRequest(principal, Execution, Context, "AgentToolSupersession", input.Explanation, now);
            var plannedMemory = new AgentMemoryItem
            {
                MemoryId = newMemoryId, TenantId = replacement.TenantId, Kind = replacement.Kind,
                Content = replacement.Content, CanonicalContentHash = replacement.CanonicalContentHash,
                PromotedAt = request.Timestamp, Confidence = replacement.Confidence,
                Status = AgentMemoryStatus.Active, IsAuthoritative = false, Tags = replacement.Tags,
                DescriptorRefs = replacement.DescriptorRefs, SourceRefs = replacement.SourceRefs,
                SupersedesMemoryId = target.MemoryId, RedactionKinds = replacement.RedactionKinds,
                SanitizationDiagnostics = replacement.SanitizationDiagnostics
            };
            memory = await _promotion.SupersedeAsync(principal.TenantId, new AgentMemorySupersessionPlan
            {
                TargetMemory = new AgentMemoryItemExpectation
                {
                    MemoryId = target.MemoryId,
                    ExpectedStateHash = _hashes.ComputeMemoryStateHash(target)
                },
                ReplacementCandidate = new AgentMemoryCandidateExpectation
                {
                    CandidateId = replacement.CandidateId,
                    ExpectedStateHash = _hashes.ComputeCandidateStateHash(replacement)
                },
                NewMemoryId = newMemoryId,
                ExpectedMemoryContentHash = replacement.CanonicalContentHash,
                ExpectedMemoryStateHash = _hashes.ComputeMemoryStateHash(plannedMemory),
                Operation = request
            }, ct).ConfigureAwait(false);

            // A confirmed commit that returns a different graph is a terminal
            // integrity failure. It is not a zero-write Conflict and must not
            // be represented by an envelope that was not preflighted.
            if (!string.Equals(memory.MemoryId, newMemoryId, StringComparison.Ordinal)
                || !string.Equals(memory.TenantId, principal.TenantId, StringComparison.Ordinal)
                || memory.Status != AgentMemoryStatus.Active
                || !string.Equals(memory.Content, replacement.Content, StringComparison.Ordinal)
                || memory.CanonicalContentHash != replacement.CanonicalContentHash
                || !string.Equals(memory.SupersedesMemoryId, target.MemoryId, StringComparison.Ordinal))
                throw new AgentMemoryPostCommitIntegrityException("Committed supersession graph differs from the preflight result.");
        }
        catch (AgentMemoryOperationException exception) when (exception.Code is
            AgentMemoryOperationFailureCode.ResourceUnavailable or
            AgentMemoryOperationFailureCode.InvalidLifecycleState or
            AgentMemoryOperationFailureCode.StateConflict or
            AgentMemoryOperationFailureCode.IdentityConflict)
        {
            await RevokeCreatedArtifactsAsync(_handles, prepared, _grants, grantResult, CancellationToken.None).ConfigureAwait(false);
            return exception.Code == AgentMemoryOperationFailureCode.ResourceUnavailable
                ? Unavailable("resource-unavailable")
                : conflict;
        }
        catch
        {
            // ConfirmedAtomic service failures are zero-write failures. Revoke
            // only artifacts created by this batch; reused artifacts remain
            // valid. A post-commit integrity exception is deliberately
            // rethrown so the Invoker fences the invocation as Indeterminate.
            await RevokeCreatedArtifactsAsync(_handles, prepared, _grants, grantResult, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return completed;
        }
        catch
        {
            await RevokeCreatedArtifactsAsync(_handles, prepared, _grants, grantResult, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static SupersedeMemoryItemResult Unavailable(string code) => new() { OperationStatus = AgentMemoryToolOperationStatus.Unavailable, Item = null, SupersededMemoryHandle = null, ActiveMemoryHandle = null, Diagnostics = [Diagnostic(code)] };
    private static SupersedeMemoryItemResult Conflict(string code) => new() { OperationStatus = AgentMemoryToolOperationStatus.Conflict, Item = null, SupersededMemoryHandle = null, ActiveMemoryHandle = null, Diagnostics = [Diagnostic(code)] };
    private static IReadOnlyList<Metadata.Abstractions.DescriptorRef> EffectiveDescriptorRefs(AgentMemoryCandidate candidate)
        => candidate.DescriptorRefs.Concat(candidate.SourceRefs.SelectMany(item => item.DescriptorRefs)).Distinct().ToArray();
}
