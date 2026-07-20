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
    private readonly IAgentMemorySourceGrantStore _grants;
    private readonly IAgentMemoryStore _store;
    private readonly IAgentMemoryPromotionService _promotion;
    private readonly IAgentMemoryArtifactIdGenerator _ids;
    private readonly AgentMemoryCanonicalHashProjector _hashes;
    private readonly TimeProvider _time;

    public SupersedeMemoryItemHandler(
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
    { _scopeProvider = scopeProvider; _handles = handles; _grants = grants; _store = store; _promotion = promotion; _ids = ids; _hashes = hashes; _time = time; }

    public async Task<SupersedeMemoryItemResult> ExecuteAsync(SupersedeMemoryItemInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope)) return Unavailable("scope-invalid");
        var targetHandle = await _handles.GetAsync(input.MemoryHandle, ct).ConfigureAwait(false);
        var replacementHandle = await _handles.GetAsync(input.ReplacementCandidateHandle, ct).ConfigureAwait(false);
        if (!Usable(targetHandle, principal, AgentMemoryResourceKind.Memory)
            || !Usable(replacementHandle, principal, AgentMemoryResourceKind.Candidate)) return Unavailable("resource-unavailable");
        var target = await _store.GetMemoryAsync(principal.TenantId, targetHandle!.ResourceId, ct).ConfigureAwait(false);
        var replacement = await _store.GetCandidateAsync(principal.TenantId, replacementHandle!.ResourceId, ct).ConfigureAwait(false);
        if (target is null || replacement is null) return Unavailable("resource-unavailable");
        if (target.Status != AgentMemoryStatus.Active || replacement.Status != AgentMemoryStatus.Candidate) return Conflict("lifecycle-conflict");
        var newMemoryId = _ids.CreateMemoryId();
        var now = _time.GetUtcNow();
        var newHandle = new AgentMemoryResourceHandle
        {
            HandleId = AgentMemorySecurityArtifactIdGenerator.Create("hnd"), ResourceKind = AgentMemoryResourceKind.Memory,
            ResourceId = newMemoryId, Principal = principal, ScopeFingerprint = targetHandle.ScopeFingerprint,
            RequiredDescriptorRefs = replacement.DescriptorRefs, IsUnscoped = replacement.DescriptorRefs.Count == 0,
            IssuingInvocationId = principal.ExecutionId, IssuedAt = now, ExpiresAt = now.Add(scope.ResourceHandleLifetime)
        };
        var planHash = ArtifactPlanHash(principal, scope, "supersede-memory", AgentMemoryResourceKind.Memory,
            newMemoryId, replacement.DescriptorRefs, replacement.SourceRefs, replacement.DescriptorRefs.Count == 0, scope.ResourceHandleLifetime);
        AgentMemoryResourceHandleIssueResult? prepared = null;
        var grantInputs = replacement.SourceRefs.Select(source => new AgentMemorySourceGrant
        {
            GrantId = AgentMemorySecurityArtifactIdGenerator.Create("grt"), SourceRef = source, Principal = principal,
            ScopeFingerprint = targetHandle.ScopeFingerprint, RequiredDescriptorRefs = replacement.DescriptorRefs.Concat(source.DescriptorRefs).Distinct().ToArray(),
            IsUnscoped = replacement.DescriptorRefs.Count == 0 && source.DescriptorRefs.Count == 0,
            IssuingInvocationId = principal.ExecutionId, IssuedAt = now, ExpiresAt = now.Add(scope.ExpansionGrantLifetime)
        }).ToArray();
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
            ("completed", completed, AgentMemoryToolJsonSerializerContext.Default.SupersedeMemoryItemResult),
            ("conflict", conflict, AgentMemoryToolJsonSerializerContext.Default.SupersedeMemoryItemResult),
            ("unavailable", unavailable, AgentMemoryToolJsonSerializerContext.Default.SupersedeMemoryItemResult));
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

        // The service has reported a committed result. Any graph mismatch is
        // an output-finalization failure, not a lifecycle conflict; do not
        // revoke artifacts or synthesize a different envelope in that case.
        if (!string.Equals(memory.MemoryId, newMemoryId, StringComparison.Ordinal)
            || !string.Equals(memory.TenantId, principal.TenantId, StringComparison.Ordinal)
            || memory.Status != AgentMemoryStatus.Active
            || !string.Equals(memory.Content, replacement.Content, StringComparison.Ordinal)
            || memory.CanonicalContentHash != replacement.CanonicalContentHash
            || !string.Equals(memory.SupersedesMemoryId, target.MemoryId, StringComparison.Ordinal))
            throw new InvalidOperationException("Committed supersession graph differs from the preflight result.");

        return completed;
    }

    private static bool Usable(AgentMemoryResourceHandle? handle, AgentMemoryToolPrincipal principal, AgentMemoryResourceKind kind)
        => handle is not null && handle.ResourceKind == kind && handle.Principal == principal && handle.State == AgentMemorySecurityArtifactState.Active && handle.ExpiresAt > DateTimeOffset.UtcNow;
    private static SupersedeMemoryItemResult Unavailable(string code) => new() { OperationStatus = AgentMemoryToolOperationStatus.Unavailable, Item = null, SupersededMemoryHandle = null, ActiveMemoryHandle = null, Diagnostics = [Diagnostic(code)] };
    private static SupersedeMemoryItemResult Conflict(string code) => new() { OperationStatus = AgentMemoryToolOperationStatus.Conflict, Item = null, SupersededMemoryHandle = null, ActiveMemoryHandle = null, Diagnostics = [Diagnostic(code)] };
}
