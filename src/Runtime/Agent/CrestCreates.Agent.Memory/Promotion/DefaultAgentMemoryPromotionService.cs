using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Identity;

namespace CrestCreates.Agent.Memory.Promotion;

public sealed class DefaultAgentMemoryPromotionService : IAgentMemoryPromotionService, IAgentMemoryCurationServiceCapabilities
{
    private readonly IAgentMemoryStore _store;
    private readonly IAgentMemoryArtifactIdGenerator _ids;
    private readonly AgentMemoryCanonicalHashProjector? _hashes;

    public DefaultAgentMemoryPromotionService(
        IAgentMemoryStore store,
        IAgentMemoryArtifactIdGenerator? ids = null,
        AgentMemoryCanonicalHashProjector? hashes = null)
    {
        _store = store;
        _ids = ids ?? new DefaultAgentMemoryArtifactIdGenerator();
        _hashes = hashes;
    }

    public AgentMemoryCurationOutcomeGuarantee OutcomeGuarantee
        => _store is IAgentMemoryStoreCapabilities capabilities
            && _store is IAgentMemoryConditionalCurationStore
            ? capabilities.CurationOutcomeGuarantee
            : AgentMemoryCurationOutcomeGuarantee.Unknown;

    public ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, AgentMemoryPromotionPlan plan, CancellationToken cancellationToken = default)
    {
        ValidateOperationRequest(tenantId, plan.Operation, nameof(PromoteAsync));
        if (_store is not IAgentMemoryConditionalCurationStore conditional)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.Unknown, "Store does not provide conditional curation transitions.");
        return conditional.PromoteAsync(tenantId, plan, CancellationToken.None);
    }

    public ValueTask RejectAsync(string tenantId, AgentMemoryCandidateExpectation candidate, AgentMemoryOperationRequest operation, CancellationToken cancellationToken = default)
    {
        ValidateOperationRequest(tenantId, operation, nameof(RejectAsync));
        if (_store is not IAgentMemoryConditionalCurationStore conditional)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.Unknown, "Store does not provide conditional curation transitions.");
        return conditional.RejectAsync(tenantId, candidate, operation, CancellationToken.None);
    }

    public ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, AgentMemorySupersessionPlan plan, CancellationToken cancellationToken = default)
    {
        ValidateOperationRequest(tenantId, plan.Operation, nameof(SupersedeAsync));
        if (_store is not IAgentMemoryConditionalCurationStore conditional)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.Unknown, "Store does not provide conditional curation transitions.");
        return conditional.SupersedeAsync(tenantId, plan, CancellationToken.None);
    }

    public ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, string candidateId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
        => PromoteAsync(tenantId, candidateId, _ids.CreateMemoryId(), request, cancellationToken);

    public ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, string candidateId, string newMemoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
    {
        ValidateOperationRequest(tenantId, request, nameof(PromoteAsync));
        var candidate = _store.GetCandidateAsync(tenantId, candidateId, cancellationToken).GetAwaiter().GetResult()
            ?? throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.ResourceUnavailable, "Candidate is unavailable.");
        if (_hashes is null || _store is not IAgentMemoryConditionalCurationStore)
            return ValueTask.FromResult(LegacyPromote(tenantId, candidate, newMemoryId, request, cancellationToken));
        var memory = CreatePromotedMemory(candidate, newMemoryId, request);
        return PromoteAsync(tenantId, new AgentMemoryPromotionPlan
        {
            Candidate = new AgentMemoryCandidateExpectation { CandidateId = candidate.CandidateId, ExpectedStateHash = _hashes.ComputeCandidateStateHash(candidate) },
            NewMemoryId = newMemoryId,
            ExpectedMemoryContentHash = memory.CanonicalContentHash,
            ExpectedMemoryStateHash = _hashes.ComputeMemoryStateHash(memory),
            Operation = request
        }, cancellationToken);
    }

    public async ValueTask RejectAsync(string tenantId, string candidateId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
    {
        ValidateOperationRequest(tenantId, request, nameof(RejectAsync));

        var candidate = await _store.GetCandidateAsync(tenantId, candidateId, cancellationToken)
            ?? throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.ResourceUnavailable, "Candidate is unavailable.");
        if (_hashes is null || _store is not IAgentMemoryConditionalCurationStore)
        {
            if (candidate.Status != AgentMemoryStatus.Candidate)
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.InvalidLifecycleState, $"Candidate has status '{candidate.Status}', expected 'Candidate'.");
            await _store.SaveCandidateAsync(candidate with { Status = AgentMemoryStatus.Rejected }, cancellationToken);
            return;
        }
        await RejectAsync(tenantId,
            new AgentMemoryCandidateExpectation { CandidateId = candidate.CandidateId, ExpectedStateHash = _hashes.ComputeCandidateStateHash(candidate) },
            request, cancellationToken);
    }

    public ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, string memoryId, AgentMemoryCandidate replacement, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
        // Compatibility overload retained for existing domain callers. Tool paths
        // must use the trusted-identity overload below.
        => SupersedeAsync(tenantId, memoryId, replacement.CandidateId, replacement.CandidateId, request, cancellationToken);

    public ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, string memoryId, string replacementCandidateId, string newMemoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
    {
        ValidateOperationRequest(tenantId, request, nameof(SupersedeAsync));
        var existing = _store.GetMemoryAsync(tenantId, memoryId, cancellationToken).GetAwaiter().GetResult()
            ?? throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.ResourceUnavailable, "Target Memory is unavailable.");
        var replacement = _store.GetCandidateAsync(tenantId, replacementCandidateId, cancellationToken).GetAwaiter().GetResult()
            ?? throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.ResourceUnavailable, "Replacement Candidate is unavailable.");
        if (_hashes is null || _store is not IAgentMemoryConditionalCurationStore)
            return ValueTask.FromResult(LegacySupersede(tenantId, existing, replacement, newMemoryId, request, cancellationToken));
        var newMemory = CreatePromotedMemory(replacement, newMemoryId, request) with { SupersedesMemoryId = existing.MemoryId };
        return SupersedeAsync(tenantId, new AgentMemorySupersessionPlan
        {
            TargetMemory = new AgentMemoryItemExpectation { MemoryId = existing.MemoryId, ExpectedStateHash = _hashes.ComputeMemoryStateHash(existing) },
            ReplacementCandidate = new AgentMemoryCandidateExpectation { CandidateId = replacement.CandidateId, ExpectedStateHash = _hashes.ComputeCandidateStateHash(replacement) },
            NewMemoryId = newMemoryId,
            ExpectedMemoryContentHash = newMemory.CanonicalContentHash,
            ExpectedMemoryStateHash = _hashes.ComputeMemoryStateHash(newMemory),
            Operation = request
        }, cancellationToken);
    }

    public async ValueTask ArchiveAsync(string tenantId, string memoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
    {
        ValidateOperationRequest(tenantId, request, nameof(ArchiveAsync));

        var memory = await _store.GetMemoryAsync(tenantId, memoryId, cancellationToken);
        if (memory is null)
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.ResourceUnavailable, "Memory is unavailable.");
        }

        if (memory.Status is not (AgentMemoryStatus.Active or AgentMemoryStatus.Superseded))
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.InvalidLifecycleState, "Memory cannot be archived from its current state.");
        }

        var archived = memory with { Status = AgentMemoryStatus.Archived };
        await _store.SaveMemoryAsync(archived, cancellationToken);
    }

    private static AgentMemoryItem CreatePromotedMemory(AgentMemoryCandidate candidate, string memoryId, AgentMemoryOperationRequest operation)
        => new()
        {
            MemoryId = memoryId,
            TenantId = candidate.TenantId,
            Kind = candidate.Kind,
            Content = candidate.Content,
            CanonicalContentHash = candidate.CanonicalContentHash,
            PromotedAt = operation.Timestamp,
            Confidence = candidate.Confidence,
            Status = AgentMemoryStatus.Active,
            IsAuthoritative = false,
            Tags = candidate.Tags,
            DescriptorRefs = candidate.DescriptorRefs,
            SourceRefs = candidate.SourceRefs,
            RedactionKinds = candidate.RedactionKinds,
            SanitizationDiagnostics = candidate.SanitizationDiagnostics
        };

    private AgentMemoryItem LegacyPromote(string tenantId, AgentMemoryCandidate candidate, string newMemoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken)
    {
        if (candidate.Status != AgentMemoryStatus.Candidate)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.InvalidLifecycleState, $"Candidate has status '{candidate.Status}', expected 'Candidate'.");
        if (string.IsNullOrWhiteSpace(newMemoryId) || _store.GetMemoryAsync(tenantId, newMemoryId, cancellationToken).GetAwaiter().GetResult() is not null)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Memory identity conflicts.");
        var memory = CreatePromotedMemory(candidate, newMemoryId, request);
        _store.SaveMemoryAsync(memory, cancellationToken).GetAwaiter().GetResult();
        _store.SaveCandidateAsync(candidate with { Status = AgentMemoryStatus.Active }, cancellationToken).GetAwaiter().GetResult();
        return memory;
    }

    private AgentMemoryItem LegacySupersede(string tenantId, AgentMemoryItem existing, AgentMemoryCandidate replacement, string newMemoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken)
    {
        if (existing.Status != AgentMemoryStatus.Active)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.InvalidLifecycleState, $"Memory has status '{existing.Status}', expected 'Active'.");
        if (replacement.Status != AgentMemoryStatus.Candidate)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.InvalidLifecycleState, $"Candidate has status '{replacement.Status}', expected 'Candidate'.");
        if (string.IsNullOrWhiteSpace(newMemoryId) || _store.GetMemoryAsync(tenantId, newMemoryId, cancellationToken).GetAwaiter().GetResult() is not null)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Memory identity conflicts.");
        var superseded = existing with { Status = AgentMemoryStatus.Superseded, SupersededByMemoryId = newMemoryId };
        var memory = CreatePromotedMemory(replacement, newMemoryId, request) with { SupersedesMemoryId = existing.MemoryId };
        _store.SaveMemoryAsync(superseded, cancellationToken).GetAwaiter().GetResult();
        _store.SaveMemoryAsync(memory, cancellationToken).GetAwaiter().GetResult();
        _store.SaveCandidateAsync(replacement with { Status = AgentMemoryStatus.Active }, cancellationToken).GetAwaiter().GetResult();
        return memory;
    }

    private static void ValidateOperationRequest(string tenantId, AgentMemoryOperationRequest request, string operationName)
    {
        // 1. Tenant match
        if (request.TenantId != tenantId)
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.TenantMismatch, $"{AgentMemoryDiagnosticCodes.InvalidOperationTenantMismatch}: operation tenant does not match the trusted tenant.");
        }

        // 2. InvocationContext
        if (request.InvocationContext is null)
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingActor, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingActor}: InvocationContext is required.");
        }
        // 2a. InvocationContext.TenantId must match operation tenantId
        if (request.InvocationContext.TenantId != tenantId)
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.TenantMismatch, $"{AgentMemoryDiagnosticCodes.InvalidOperationTenantMismatch}: InvocationContext tenant does not match the trusted tenant.");
        }
        if (string.IsNullOrWhiteSpace(request.InvocationContext.ActorId))
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingActor, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingActor}: InvocationContext.ActorId is required.");
        }
        if (string.IsNullOrWhiteSpace(request.InvocationContext.ActorKind))
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingActor, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingActor}: InvocationContext.ActorKind is required.");
        }

        // 3. Reason
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingReason, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingReason}: Reason is required.");
        }

        // 4. Timestamp
        if (request.Timestamp == default(DateTimeOffset))
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingTimestamp, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingTimestamp}: Timestamp is required and must not be default.");
        }

        // 5. SourceRefs or Explanation
        if (request.SourceRefs.Count == 0 && string.IsNullOrWhiteSpace(request.Explanation))
        {
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.MissingSourceOrExplanation, $"{AgentMemoryDiagnosticCodes.InvalidOperationMissingSourceOrExplanation}: SourceRefs or Explanation is required.");
        }
    }
}
