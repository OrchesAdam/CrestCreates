using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

[CapabilityName(AgentMemoryToolCapabilityIds.RejectCandidate)]
internal sealed class RejectMemoryCandidateHandler : AgentMemoryToolHandlerBase, ICapabilityHandler<RejectMemoryCandidateInput, RejectMemoryCandidateResult>
{
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemoryResourceHandleStore _handles;
    private readonly IAgentMemoryStore _store;
    private readonly IAgentMemoryPromotionService _promotion;
    private readonly AgentMemoryCanonicalHashProjector _hashes;
    private readonly TimeProvider _time;

    public RejectMemoryCandidateHandler(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemoryResourceHandleStore handles,
        IAgentMemoryStore store,
        IAgentMemoryPromotionService promotion,
        AgentMemoryCanonicalHashProjector hashes,
        TimeProvider time)
        : base(capabilityContext, agentExecution)
    { _scopeProvider = scopeProvider; _handles = handles; _store = store; _promotion = promotion; _hashes = hashes; _time = time; }

    public async Task<RejectMemoryCandidateResult> ExecuteAsync(RejectMemoryCandidateInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope)) return Unavailable("scope-invalid");
        var handle = await _handles.GetAsync(input.CandidateHandle, ct).ConfigureAwait(false);
        if (handle is null || handle.ResourceKind != AgentMemoryResourceKind.Candidate || handle.Principal != principal
            || handle.State != AgentMemorySecurityArtifactState.Active || handle.ExpiresAt <= _time.GetUtcNow())
            return Unavailable("candidate-unavailable");
        var candidate = await _store.GetCandidateAsync(principal.TenantId, handle.ResourceId, ct).ConfigureAwait(false);
        if (candidate is null) return Unavailable("candidate-unavailable");
        if (candidate.Status != AgentMemoryStatus.Candidate) return Conflict("candidate-consumed");
        var completed = new RejectMemoryCandidateResult
        {
            OperationStatus = AgentMemoryToolOperationStatus.Completed,
            CandidateHandle = input.CandidateHandle,
            CandidateStatus = AgentMemoryToolCandidateStatus.Rejected,
            IsAuthoritative = false,
            Diagnostics = Array.Empty<AgentMemoryToolDiagnosticDto>()
        };
        var conflict = Conflict("candidate-transition-conflict");
        var unavailable = Unavailable("candidate-unavailable");
        AddBranchInvariantFacts(scope, "reject-memory-candidate");
        PublishAllowedOutcomes(
            ("completed", completed, AgentMemoryToolJsonSerializerContext.Default.RejectMemoryCandidateResult),
            ("conflict", conflict, AgentMemoryToolJsonSerializerContext.Default.RejectMemoryCandidateResult),
            ("unavailable", unavailable, AgentMemoryToolJsonSerializerContext.Default.RejectMemoryCandidateResult));
        try
        {
            var request = AgentMemoryCurationHandlerHelpers.CreateRequest(principal, Execution, Context, "AgentToolRejection", input.Explanation, _time.GetUtcNow());
            await _promotion.RejectAsync(principal.TenantId,
                new AgentMemoryCandidateExpectation
                {
                    CandidateId = candidate.CandidateId,
                    ExpectedStateHash = _hashes.ComputeCandidateStateHash(candidate)
                }, request, ct).ConfigureAwait(false);
            return completed;
        }
        catch (AgentMemoryOperationException exception) when (exception.Code is
            AgentMemoryOperationFailureCode.ResourceUnavailable or
            AgentMemoryOperationFailureCode.InvalidLifecycleState or
            AgentMemoryOperationFailureCode.StateConflict or
            AgentMemoryOperationFailureCode.IdentityConflict)
        { return exception.Code == AgentMemoryOperationFailureCode.ResourceUnavailable ? unavailable : conflict; }
    }

    private static RejectMemoryCandidateResult Conflict(string code) => new() { OperationStatus = AgentMemoryToolOperationStatus.Conflict, CandidateHandle = null, CandidateStatus = null, IsAuthoritative = false, Diagnostics = [Diagnostic(code)] };
    private static RejectMemoryCandidateResult Unavailable(string code) => new() { OperationStatus = AgentMemoryToolOperationStatus.Unavailable, CandidateHandle = null, CandidateStatus = null, IsAuthoritative = false, Diagnostics = [Diagnostic(code)] };
}
