using System.Collections.Concurrent;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Stores;

public sealed class InMemoryAgentMemoryStore : IAgentMemoryStore, IAgentMemoryStoreCapabilities, IAgentMemoryConditionalCurationStore
{
    private readonly object _gate = new();
    private readonly AgentMemoryCanonicalHashProjector? _stateHashes;
    private readonly ConcurrentDictionary<(string TenantId, string CandidateId), AgentMemoryCandidate> _candidates = new();
    private readonly ConcurrentDictionary<(string TenantId, string MemoryId), AgentMemoryItem> _memories = new();

    public InMemoryAgentMemoryStore(AgentMemoryCanonicalHashProjector? stateHashes = null)
    {
        _stateHashes = stateHashes;
    }

    public AgentMemoryCurationOutcomeGuarantee CurationOutcomeGuarantee
        => AgentMemoryCurationOutcomeGuarantee.ConfirmedAtomic;

    public ValueTask SaveCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            if (_candidates.ContainsKey((candidate.TenantId, candidate.CandidateId)))
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Candidate identity already exists; use a conditional lifecycle transition.");
            _candidates[(candidate.TenantId, candidate.CandidateId)] = candidate.Snapshot();
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask CreateCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default)
        => CreateCandidatesAsync([candidate], cancellationToken);

    public ValueTask CreateCandidatesAsync(IReadOnlyList<AgentMemoryCandidate> candidates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0 || candidates.Any(item => item is null))
            throw new ArgumentException("At least one Candidate is required.", nameof(candidates));
        lock (_gate)
        {
            if (candidates.Select(item => (item.TenantId, item.CandidateId)).Distinct().Count() != candidates.Count
                || candidates.Any(item => _candidates.ContainsKey((item.TenantId, item.CandidateId))))
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Candidate identity already exists.");
            foreach (var candidate in candidates)
                _candidates[(candidate.TenantId, candidate.CandidateId)] = candidate.Snapshot();
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask TransitionCandidateStatusAsync(
        string tenantId,
        string candidateId,
        AgentMemoryStatus expectedStatus,
        AgentMemoryStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_candidates.TryGetValue((tenantId, candidateId), out var candidate))
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.ResourceUnavailable, "Candidate is unavailable.");
            if (candidate.Status != expectedStatus)
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.InvalidLifecycleState, "Candidate lifecycle state changed.");
            _candidates[(tenantId, candidateId)] = candidate with { Status = newStatus };
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentMemoryCandidate?> GetCandidateAsync(string tenantId, string candidateId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_candidates.TryGetValue((tenantId, candidateId), out var candidate)
                ? (AgentMemoryCandidate?)candidate.Snapshot()
                : null);
        }
    }

    public ValueTask SaveMemoryAsync(AgentMemoryItem memory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        lock (_gate)
        {
            if (_memories.TryGetValue((memory.TenantId, memory.MemoryId), out var existing)
                && !EquivalentMemoryPayload(existing, memory))
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.StateConflict, "Memory payload is immutable after creation.");
            _memories[(memory.TenantId, memory.MemoryId)] = memory.Snapshot();
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentMemoryItem?> GetMemoryAsync(string tenantId, string memoryId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_memories.TryGetValue((tenantId, memoryId), out var memory)
                ? (AgentMemoryItem?)memory.Snapshot()
                : null);
        }
    }

    public ValueTask<IReadOnlyList<AgentMemoryItem>> ListMemoriesAsync(AgentMemoryQuery query, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var results = _memories.Values
                .Where(m => m.TenantId == query.TenantId)
                .Where(m => query.Kinds.Count == 0 || query.Kinds.Contains(m.Kind))
                .Where(m => query.Tags.Count == 0 || query.Tags.Any(t => m.Tags.Contains(t)))
                .Where(m => query.MemoryIds.Count == 0 || query.MemoryIds.Contains(m.MemoryId))
                .Where(m => FilterByDescriptorRefs(m, query))
                .Where(m => FilterByStatus(m, query))
                .OrderBy(m => m.MemoryId, StringComparer.Ordinal)
                .Select(m => m.Snapshot())
                .ToArray();
            return ValueTask.FromResult<IReadOnlyList<AgentMemoryItem>>(results);
        }
    }

    public ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, AgentMemoryPromotionPlan plan, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var hashes = RequireHashes();
            var candidate = GetCandidateUnsafe(tenantId, plan.Candidate.CandidateId)
                ?? throw Unavailable("Candidate is unavailable.");
            EnsureTenant(candidate.TenantId, tenantId);
            EnsureCandidateExpectation(candidate, plan.Candidate, hashes);
            if (candidate.Status != AgentMemoryStatus.Candidate)
                throw InvalidLifecycle("Candidate is not in Candidate state.");
            if (string.IsNullOrWhiteSpace(plan.NewMemoryId) || _memories.ContainsKey((tenantId, plan.NewMemoryId)))
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Memory identity conflicts.");
            if (!candidate.CanonicalContentHash.Equals(plan.ExpectedMemoryContentHash))
                throw StateConflict("Prepared content hash does not match Candidate payload.");

            var memory = CreatePromotedMemory(candidate, plan.NewMemoryId, plan.Operation);
            EnsureExpectedMemory(memory, plan.ExpectedMemoryStateHash, hashes);
            _memories[(tenantId, memory.MemoryId)] = memory.Snapshot();
            _candidates[(tenantId, candidate.CandidateId)] = candidate with { Status = AgentMemoryStatus.Active };
            return ValueTask.FromResult(memory.Snapshot());
        }
    }

    public ValueTask RejectAsync(string tenantId, AgentMemoryCandidateExpectation expectation, AgentMemoryOperationRequest operation, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var hashes = RequireHashes();
            var candidate = GetCandidateUnsafe(tenantId, expectation.CandidateId)
                ?? throw Unavailable("Candidate is unavailable.");
            EnsureTenant(candidate.TenantId, tenantId);
            EnsureCandidateExpectation(candidate, expectation, hashes);
            if (candidate.Status != AgentMemoryStatus.Candidate)
                throw InvalidLifecycle("Candidate is not in Candidate state.");
            _candidates[(tenantId, candidate.CandidateId)] = candidate with { Status = AgentMemoryStatus.Rejected };
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, AgentMemorySupersessionPlan plan, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var hashes = RequireHashes();
            var existing = GetMemoryUnsafe(tenantId, plan.TargetMemory.MemoryId)
                ?? throw Unavailable("Target Memory is unavailable.");
            EnsureTenant(existing.TenantId, tenantId);
            EnsureMemoryExpectation(existing, plan.TargetMemory, hashes);
            if (existing.Status != AgentMemoryStatus.Active)
                throw InvalidLifecycle("Target Memory is not Active.");
            var replacement = GetCandidateUnsafe(tenantId, plan.ReplacementCandidate.CandidateId)
                ?? throw Unavailable("Replacement Candidate is unavailable.");
            EnsureTenant(replacement.TenantId, tenantId);
            EnsureCandidateExpectation(replacement, plan.ReplacementCandidate, hashes);
            if (replacement.Status != AgentMemoryStatus.Candidate)
                throw InvalidLifecycle("Replacement Candidate is not in Candidate state.");
            if (string.IsNullOrWhiteSpace(plan.NewMemoryId) || _memories.ContainsKey((tenantId, plan.NewMemoryId)))
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Memory identity conflicts.");
            if (!replacement.CanonicalContentHash.Equals(plan.ExpectedMemoryContentHash))
                throw StateConflict("Prepared content hash does not match Candidate payload.");

            var superseded = existing with { Status = AgentMemoryStatus.Superseded, SupersededByMemoryId = plan.NewMemoryId };
            var memory = CreatePromotedMemory(replacement, plan.NewMemoryId, plan.Operation) with { SupersedesMemoryId = existing.MemoryId };
            EnsureExpectedMemory(memory, plan.ExpectedMemoryStateHash, hashes);
            _memories[(tenantId, existing.MemoryId)] = superseded.Snapshot();
            _memories[(tenantId, memory.MemoryId)] = memory.Snapshot();
            _candidates[(tenantId, replacement.CandidateId)] = replacement with { Status = AgentMemoryStatus.Active };
            return ValueTask.FromResult(memory.Snapshot());
        }
    }

    private AgentMemoryCanonicalHashProjector RequireHashes()
        => _stateHashes ?? throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.Unknown, "State hash projector is unavailable.");

    private AgentMemoryCandidate? GetCandidateUnsafe(string tenantId, string id)
        => _candidates.TryGetValue((tenantId, id), out var candidate) ? candidate : null;

    private AgentMemoryItem? GetMemoryUnsafe(string tenantId, string id)
        => _memories.TryGetValue((tenantId, id), out var memory) ? memory : null;

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

    private static void EnsureCandidateExpectation(AgentMemoryCandidate candidate, AgentMemoryCandidateExpectation expectation, AgentMemoryCanonicalHashProjector hashes)
    {
        if (!string.Equals(candidate.CandidateId, expectation.CandidateId, StringComparison.Ordinal)
            || !hashes.ComputeCandidateStateHash(candidate).Equals(expectation.ExpectedStateHash))
            throw StateConflict("Candidate state changed since preparation.");
    }

    private static void EnsureMemoryExpectation(AgentMemoryItem memory, AgentMemoryItemExpectation expectation, AgentMemoryCanonicalHashProjector hashes)
    {
        if (!string.Equals(memory.MemoryId, expectation.MemoryId, StringComparison.Ordinal)
            || !hashes.ComputeMemoryStateHash(memory).Equals(expectation.ExpectedStateHash))
            throw StateConflict("Memory state changed since preparation.");
    }

    private static void EnsureExpectedMemory(AgentMemoryItem memory, CanonicalHash expected, AgentMemoryCanonicalHashProjector hashes)
    {
        if (!hashes.ComputeMemoryStateHash(memory).Equals(expected))
            throw StateConflict("Prepared Memory state does not match the committed graph.");
    }

    private static void EnsureTenant(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.TenantMismatch, "Tenant mismatch.");
    }

    private static AgentMemoryOperationException Unavailable(string message)
        => new(AgentMemoryOperationFailureCode.ResourceUnavailable, message);

    private static AgentMemoryOperationException InvalidLifecycle(string message)
        => new(AgentMemoryOperationFailureCode.InvalidLifecycleState, message);

    private static AgentMemoryOperationException StateConflict(string message)
        => new(AgentMemoryOperationFailureCode.StateConflict, message);

    private static bool EquivalentPayload(AgentMemoryCandidate left, AgentMemoryCandidate right)
        => left.TenantId == right.TenantId
            && left.CandidateId == right.CandidateId
            && left.Kind == right.Kind
            && left.Content == right.Content
            && left.CanonicalContentHash.Equals(right.CanonicalContentHash)
            && left.Confidence == right.Confidence
            && left.Tags.SequenceEqual(right.Tags)
            && left.DescriptorRefs.SequenceEqual(right.DescriptorRefs)
            && left.SourceRefs.SequenceEqual(right.SourceRefs)
            && left.RedactionKinds.SequenceEqual(right.RedactionKinds)
            && left.SanitizationDiagnostics.SequenceEqual(right.SanitizationDiagnostics)
            && Equals(left.PromptInputEvidence, right.PromptInputEvidence)
            && Equals(left.PromptOutputEvidence, right.PromptOutputEvidence)
            && Equals(left.CanonicalOutputHash, right.CanonicalOutputHash);

    private static bool EquivalentMemoryPayload(AgentMemoryItem left, AgentMemoryItem right)
        => left.TenantId == right.TenantId
            && left.MemoryId == right.MemoryId
            && left.Kind == right.Kind
            && left.Content == right.Content
            && left.CanonicalContentHash.Equals(right.CanonicalContentHash)
            && left.PromotedAt == right.PromotedAt
            && left.Confidence == right.Confidence
            && left.IsAuthoritative == right.IsAuthoritative
            && left.Tags.SequenceEqual(right.Tags)
            && left.DescriptorRefs.SequenceEqual(right.DescriptorRefs)
            && left.SourceRefs.SequenceEqual(right.SourceRefs)
            && left.RedactionKinds.SequenceEqual(right.RedactionKinds)
            && left.SanitizationDiagnostics.SequenceEqual(right.SanitizationDiagnostics)
            ;

    private static bool FilterByStatus(AgentMemoryItem memory, AgentMemoryQuery query)
        => memory.Status switch
        {
            AgentMemoryStatus.Active => true,
            AgentMemoryStatus.Superseded => query.IncludeSuperseded,
            AgentMemoryStatus.Archived => query.IncludeArchived,
            AgentMemoryStatus.Candidate => false,
            _ => false
        };

    private static bool FilterByDescriptorRefs(AgentMemoryItem memory, AgentMemoryQuery query)
    {
        if (query.DescriptorRefs.Count == 0) return true;
        return query.DescriptorRefs.Any(qr => memory.DescriptorRefs.Any(mr => mr.Equals(qr)));
    }
}
