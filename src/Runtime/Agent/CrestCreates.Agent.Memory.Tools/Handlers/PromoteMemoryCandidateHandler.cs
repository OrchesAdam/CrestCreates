using CrestCreates.Agent.Abstractions;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Identity;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

[CapabilityName(AgentMemoryToolCapabilityIds.PromoteCandidate)]
internal sealed class PromoteMemoryCandidateHandler : AgentMemoryToolHandlerBase, ICapabilityHandler<PromoteMemoryCandidateInput, PromoteMemoryCandidateResult>
{
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemoryResourceHandleResolver _handleResolver;
    private readonly IAgentMemorySecurityArtifactCoordinator _artifacts;
    private readonly IAgentMemoryPromotionService _promotion;
    private readonly IAgentMemoryArtifactIdGenerator _ids;
    private readonly IAgentMemoryOperationIdentityFactory _identities;
    private readonly AgentMemoryCanonicalHashProjector _hashes;
    private readonly TimeProvider _time;

    public PromoteMemoryCandidateHandler(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution,
        IAuditOperationContextAccessor auditContexts,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemoryResourceHandleResolver handleResolver,
        IAgentMemorySecurityArtifactCoordinator artifacts,
        AgentMemoryToolRuntimeBinding runtimeBinding,
        IAgentMemoryArtifactIdGenerator ids,
        AgentMemoryCanonicalHashProjector hashes,
        IAgentMemoryOperationIdentityFactory identities,
        TimeProvider time)
        : base(capabilityContext, agentExecution, auditContexts)
    {
        _scopeProvider = scopeProvider; _handleResolver = handleResolver; _artifacts = artifacts; _promotion = runtimeBinding.PromotionService; _ids = ids; _hashes = hashes; _identities = identities; _time = time;
    }

    public async Task<PromoteMemoryCandidateResult> ExecuteAsync(PromoteMemoryCandidateInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope)) return PrepareOutcome(scope, "promote-memory-candidate", "unavailable", Unavailable("scope-invalid"));
        var resolved = await _handleResolver.ResolveAsync(input.CandidateHandle, AgentMemoryResourceKind.Candidate, principal, scope, ct).ConfigureAwait(false);
        if (resolved?.Resource is not AgentMemoryCandidate candidate) return PrepareOutcome(scope, "promote-memory-candidate", "unavailable", Unavailable("candidate-unavailable"));
        var handle = resolved.Handle;
        if (candidate.Status != AgentMemoryStatus.Candidate) return PrepareOutcome(scope, "promote-memory-candidate", "conflict", Conflict("candidate-consumed"));
        var newMemoryId = _ids.CreateMemoryId();
        var now = _time.GetUtcNow();
        var identity = _identities.Create();
        var itemHandle = new AgentMemoryResourceHandle
        {
            HandleId = AgentMemorySecurityArtifactIdGenerator.Create("hnd"), ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = newMemoryId, Principal = principal, ScopeFingerprint = AgentMemoryToolScopeFingerprint.Compute(scope, principal),
            RequiredDescriptorRefs = EffectiveDescriptorRefs(candidate), IsUnscoped = EffectiveDescriptorRefs(candidate).Count == 0,
            IssuingInvocationId = InvocationBinding.LogicalKey.InvocationId, IssuedAt = now, ExpiresAt = now.Add(scope.ResourceHandleLifetime)
        };
        var grantsInput = candidate.SourceRefs.Select(source =>
        {
            var requiredDescriptorRefs = AgentMemoryHandleGrantMatrix.GetRequiredDescriptorRefs(
                source.SourceKind,
                source.DescriptorRefs);
            return new AgentMemorySourceGrant
            {
                GrantId = AgentMemorySecurityArtifactIdGenerator.Create("grt"),
                SourceRef = source,
                Principal = principal,
                ScopeFingerprint = AgentMemoryToolScopeFingerprint.Compute(scope, principal),
                RequiredDescriptorRefs = requiredDescriptorRefs,
                IsUnscoped = AgentMemoryHandleGrantMatrix.IsUnscopedGrant(
                    source.SourceKind,
                    requiredDescriptorRefs),
                IssuingInvocationId = InvocationBinding.LogicalKey.InvocationId,
                IssuedAt = now,
                ExpiresAt = now.Add(scope.ExpansionGrantLifetime)
            };
        }).ToArray();
        AgentMemoryPreparedSecurityArtifacts? prepared = null;
        try
        {
            prepared = await _artifacts.PrepareForAgentToolAsync(
                InvocationBinding, principal, scope, "promote-memory", 0, [itemHandle], grantsInput, ct).ConfigureAwait(false);
        }
        catch { throw; }
        try
        {
        var preparedHandle = prepared.Handles;
        var preparedGrantResult = prepared.Grants;
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
            ("completed", PrepareOutput(completed)),
            ("conflict", PrepareOutput(conflict)),
            ("unavailable", PrepareOutput(unavailable)));
        AgentMemoryItem memory;
        try
        {
            var request = AgentMemoryCurationHandlerHelpers.CreateRequest(principal, Context, "AgentToolPromotion", input.Explanation, identity, AmbientAudit);
            var plannedMemory = new AgentMemoryItem
            {
                MemoryId = newMemoryId, TenantId = candidate.TenantId, Kind = candidate.Kind,
                Content = candidate.Content, CanonicalContentHash = candidate.CanonicalContentHash,
                PromotedAt = request.Identity.OccurredAt, Confidence = candidate.Confidence,
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
            await _artifacts.RevokeCreatedAsync(prepared, CancellationToken.None).ConfigureAwait(false);
            // Resource disappearance is a confirmed zero-write unavailable
            // branch; lifecycle/expectation races are normal Conflict.
            return exception.Code == AgentMemoryOperationFailureCode.ResourceUnavailable
                ? unavailable
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
            await _artifacts.RevokeCreatedAsync(prepared, CancellationToken.None).ConfigureAwait(false);
            throw new AgentMemoryPostCommitIntegrityException("Committed promotion graph differs from the preflight result.");
        }
        return completed;
        }
        catch
        {
            await _artifacts.RevokeCreatedAsync(prepared, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static IReadOnlyList<Metadata.Abstractions.DescriptorRef> EffectiveDescriptorRefs(AgentMemoryCandidate candidate)
        => candidate.DescriptorRefs.Concat(candidate.SourceRefs.SelectMany(item => item.DescriptorRefs)).Distinct().ToArray();
    private static PromoteMemoryCandidateResult Unavailable(string code) => new() { OperationStatus = AgentMemoryToolOperationStatus.Unavailable, Item = null, Diagnostics = [Diagnostic(code)] };
    private static PromoteMemoryCandidateResult Conflict(string code) => new() { OperationStatus = AgentMemoryToolOperationStatus.Conflict, Item = null, Diagnostics = [Diagnostic(code)] };
}
