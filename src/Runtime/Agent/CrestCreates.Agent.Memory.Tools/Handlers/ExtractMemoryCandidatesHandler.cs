using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

[CapabilityName(AgentMemoryToolCapabilityIds.ExtractCandidates)]
internal sealed class ExtractMemoryCandidatesHandler : AgentMemoryToolHandlerBase, ICapabilityHandler<ExtractMemoryCandidatesInput, ExtractMemoryCandidatesResult>
{
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemoryResourceHandleStore _handles;
    private readonly IAgentMemoryResourceHandleResolver _handleResolver;
    private readonly IAgentMemorySourceGrantStore _grants;
    private readonly IAgentCompressedContextStore _contexts;
    private readonly IAgentMemoryExtractor _extractor;
    private readonly IAgentMemoryStore _memoryStore;
    private readonly IAgentMemoryContentSanitizer _sanitizer;
    private readonly IAgentMemoryArtifactIdGenerator _ids;
    private readonly TimeProvider _time;

    public ExtractMemoryCandidatesHandler(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemoryResourceHandleStore handles,
        IAgentMemoryResourceHandleResolver handleResolver,
        IAgentMemorySourceGrantStore grants,
        IAgentCompressedContextStore contexts,
        IAgentMemoryExtractor extractor,
        IAgentMemoryStore memoryStore,
        IAgentMemoryContentSanitizer sanitizer,
        IAgentMemoryArtifactIdGenerator ids,
        TimeProvider time)
        : base(capabilityContext, agentExecution)
    {
        _scopeProvider = scopeProvider; _handles = handles; _grants = grants; _contexts = contexts;
        _handleResolver = handleResolver;
        _extractor = extractor; _memoryStore = memoryStore; _sanitizer = sanitizer; _ids = ids; _time = time;
    }

    public async Task<ExtractMemoryCandidatesResult> ExecuteAsync(ExtractMemoryCandidatesInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope)) return PrepareOutcome(scope, "extract-memory-candidates", "unavailable", Unavailable("scope-invalid"), AgentMemoryToolJsonSerializerContext.Default.ExtractMemoryCandidatesResult);
        var resolvedHandle = await _handleResolver.ResolveAsync(input.ContextHandle, AgentMemoryResourceKind.Context, principal, scope, ct).ConfigureAwait(false);
        var handle = resolvedHandle?.Handle;
        if (handle is null)
            return PrepareOutcome(scope, "extract-memory-candidates", "unavailable", Unavailable("context-unavailable"), AgentMemoryToolJsonSerializerContext.Default.ExtractMemoryCandidatesResult);
        var context = await _contexts.GetCompressedContextAsync(principal.TenantId, handle.ResourceId, ct).ConfigureAwait(false);
        if (context is null || context.Blocks.Any(block => block.SourceRefs.Count > scope.MaxSourceRefsPerArtifact))
            return PrepareOutcome(scope, "extract-memory-candidates", "unavailable", Unavailable("context-unavailable"), AgentMemoryToolJsonSerializerContext.Default.ExtractMemoryCandidatesResult);
        var allowedSourceRefs = context.Blocks.SelectMany(block => block.SourceRefs).ToArray();
        var allowedDescriptorRefs = allowedSourceRefs.SelectMany(source => source.DescriptorRefs).ToArray();
        var providerCandidates = (await _extractor.ExtractCandidatesAsync(context, ct).ConfigureAwait(false)).ToArray();
        if (providerCandidates.Length > scope.MaxCandidateCount
            || providerCandidates.Any(candidate => candidate.TenantId != principal.TenantId
                || candidate.Status != AgentMemoryStatus.Candidate
                || candidate.Content.Length > scope.MaxCandidateCharacters
                || candidate.SourceRefs.Count > scope.MaxSourceRefsPerArtifact
                || candidate.Tags.Count > scope.MaxTagsPerResource
                || !IsTrustedSourceRefSubset(candidate.SourceRefs, allowedSourceRefs)
                || candidate.DescriptorRefs.Any(reference => !allowedDescriptorRefs.Contains(reference))))
            return PrepareOutcome(scope, "extract-memory-candidates", "unavailable", Unavailable("result-invalid"), AgentMemoryToolJsonSerializerContext.Default.ExtractMemoryCandidatesResult);

        var candidates = new List<AgentMemoryCandidate>(providerCandidates.Length);
        foreach (var providerCandidate in providerCandidates)
        {
            var sanitized = _sanitizer.Sanitize(principal.TenantId, providerCandidate.Content, providerCandidate.SourceRefs);
            if (sanitized.Rejected)
                return PrepareOutcome(scope, "extract-memory-candidates", "unavailable", Unavailable("result-invalid"), AgentMemoryToolJsonSerializerContext.Default.ExtractMemoryCandidatesResult);
            candidates.Add(providerCandidate with
            {
                CandidateId = _ids.CreateCandidateId(),
                TenantId = principal.TenantId,
                Content = sanitized.SanitizedContent,
                CanonicalContentHash = sanitized.CanonicalContentHash,
                RedactionKinds = providerCandidate.RedactionKinds.Concat(sanitized.RedactionKinds).Distinct(StringComparer.Ordinal).ToArray(),
                SanitizationDiagnostics = providerCandidate.SanitizationDiagnostics.Concat(sanitized.Diagnostics).ToArray()
            });
        }
        var now = _time.GetUtcNow();
        var scopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope, principal);
        var candidateHandles = candidates.Select(candidate => new AgentMemoryResourceHandle
        {
            HandleId = AgentMemorySecurityArtifactIdGenerator.Create("hnd"), ResourceKind = AgentMemoryResourceKind.Candidate,
            ResourceId = candidate.CandidateId, Principal = principal, ScopeFingerprint = scopeFingerprint,
            RequiredDescriptorRefs = EffectiveDescriptorRefs(candidate), IsUnscoped = EffectiveDescriptorRefs(candidate).Count == 0,
            IssuingInvocationId = principal.ExecutionId, IssuedAt = now, ExpiresAt = now.Add(scope.ResourceHandleLifetime)
        }).ToArray();
        var grantsInput = candidates.SelectMany(candidate => candidate.SourceRefs.Select(source => new AgentMemorySourceGrant
        {
            GrantId = AgentMemorySecurityArtifactIdGenerator.Create("grt"), SourceRef = source, Principal = principal,
            ScopeFingerprint = scopeFingerprint, RequiredDescriptorRefs = candidate.DescriptorRefs.Concat(source.DescriptorRefs).Distinct().ToArray(),
            IsUnscoped = candidate.DescriptorRefs.Count == 0 && source.DescriptorRefs.Count == 0,
            IssuingInvocationId = principal.ExecutionId, IssuedAt = now, ExpiresAt = now.Add(scope.ExpansionGrantLifetime)
        })).ToArray();
        var artifactPlanHash = AgentMemoryArtifactPlanProjector.Compute(principal, scope, "extract-candidates", candidateHandles, grantsInput);
        AgentMemoryResourceHandleIssueResult? issuedHandleResult = null;
        try
        {
            issuedHandleResult = candidateHandles.Length == 0 ? null
                : await _handles.TryIssueBatchAsync(AgentToolBatchKey(Context, "candidate-handles", artifactPlanHash), candidateHandles, scope.MaxActiveResourceHandlesPerResource, scope.MaxResourceHandlesPerInvocation, ct).ConfigureAwait(false);
        }
        catch
        {
            throw;
        }
        var issuedHandles = issuedHandleResult?.Handles.ToArray() ?? Array.Empty<AgentMemoryResourceHandle>();
        AgentMemoryGrantIssueResult? grantResult = null;
        try
        {
            grantResult = grantsInput.Length == 0 ? null
                : await _grants.TryIssueBatchAsync(AgentToolBatchKey(Context, "candidate-grants", artifactPlanHash), grantsInput, scope.MaxGrantsPerResource, scope.MaxGrantsPerInvocation, ct).ConfigureAwait(false);
        }
        catch
        {
            await RevokeCreatedArtifactsAsync(_handles, issuedHandleResult, _grants, grantResult, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        try
        {
            var grants = grantResult?.Grants.ToArray() ?? Array.Empty<AgentMemorySourceGrant>();
            var handleById = issuedHandles.ToDictionary(item => item.ResourceId, StringComparer.Ordinal);
            var grantsBySource = grants.ToLookup(item => item.SourceRef, AgentContextSourceRefCanonicalComparer.Instance);
            var result = new ExtractMemoryCandidatesResult
            {
            OperationStatus = AgentMemoryToolOperationStatus.Completed,
            ContextHandle = input.ContextHandle,
            Candidates = candidates.Select(candidate => new AgentMemoryToolCandidateDto
            {
                CandidateHandle = handleById[candidate.CandidateId].HandleId,
                Kind = AgentMemoryToolProjection.ToToolKind(candidate.Kind), Content = candidate.Content,
                CanonicalContentHash = AgentMemoryToolProjection.ToToolHash(candidate.CanonicalContentHash),
                Confidence = AgentMemoryToolProjection.ToToolConfidence(candidate.Confidence),
                CandidateStatus = AgentMemoryToolCandidateStatus.Candidate, IsAuthoritative = false,
                SourceGrants = candidate.SourceRefs.SelectMany(source => grantsBySource[source].Select(item => new AgentMemorySourceGrantDto
                {
                    GrantId = item.GrantId,
                    SourceKind = AgentMemoryToolProjection.ToToolSourceKind(item.SourceRef.SourceKind),
                    ExpiresAt = item.ExpiresAt
                })).ToArray()
            }).ToArray(),
            CandidateCount = candidates.Count,
            Diagnostics = Array.Empty<AgentMemoryToolDiagnosticDto>()
            };
            AddBranchInvariantFacts(scope, "extract-memory-candidates");
            PublishAllowedOutcomes(("completed", PrepareOutput(result, AgentMemoryToolJsonSerializerContext.Default.ExtractMemoryCandidatesResult)));
            foreach (var candidate in candidates)
                await _memoryStore.SaveCandidateAsync(candidate, ct).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await RevokeCreatedArtifactsAsync(_handles, issuedHandleResult, _grants, grantResult, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static ExtractMemoryCandidatesResult Unavailable(string code) => new()
    {
        OperationStatus = AgentMemoryToolOperationStatus.Unavailable, ContextHandle = null,
        Candidates = Array.Empty<AgentMemoryToolCandidateDto>(), CandidateCount = 0, Diagnostics = [Diagnostic(code)]
    };

    private static IReadOnlyList<CrestCreates.Metadata.Abstractions.DescriptorRef> EffectiveDescriptorRefs(AgentMemoryCandidate candidate)
        => candidate.DescriptorRefs.Concat(candidate.SourceRefs.SelectMany(item => item.DescriptorRefs)).Distinct().ToArray();

}
