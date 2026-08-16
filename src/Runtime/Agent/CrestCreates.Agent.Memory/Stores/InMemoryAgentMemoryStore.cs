using System.Collections.Concurrent;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Agent.Memory.Abstractions.Curation;
using CrestCreates.Agent.Memory.Abstractions.Persistence;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Curation;
using CrestCreates.Agent.Memory.Persistence;

namespace CrestCreates.Agent.Memory.Stores;

public sealed class InMemoryAgentMemoryStore : IAgentMemoryStore, IAgentMemoryStoreCapabilities, IAgentMemoryConditionalCurationStore
{
    private readonly object _gate = new();
    private readonly IAgentMemoryStateHashProjector? _stateHashes;
    private readonly IAgentMemoryCurationStateMachine _stateMachine;
    private readonly IAgentMemoryPersistenceComparer _comparer;
    private readonly ConcurrentDictionary<(string TenantId, string CandidateId), AgentMemoryCandidate> _candidates = new();
    private readonly ConcurrentDictionary<(string TenantId, string MemoryId), AgentMemoryItem> _memories = new();

    public InMemoryAgentMemoryStore(
        IAgentMemoryStateHashProjector? stateHashes = null,
        IAgentMemoryCurationStateMachine? stateMachine = null,
        IAgentMemoryPersistenceComparer? comparer = null)
    {
        _stateHashes = stateHashes;
        _stateMachine = stateMachine ?? new DefaultAgentMemoryCurationStateMachine(
            stateHashes ?? new UnavailableStateHashProjector(),
            new DefaultAgentMemoryCurationProjector());
        _comparer = comparer ?? new DefaultAgentMemoryPersistenceComparer();
    }

    public AgentMemoryCurationOutcomeGuarantee CurationOutcomeGuarantee
        => AgentMemoryCurationOutcomeGuarantee.ConfirmedAtomic;

    public ValueTask SaveCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
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
        cancellationToken.ThrowIfCancellationRequested();
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
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_memories.TryGetValue((memory.TenantId, memory.MemoryId), out var existing))
            {
                if (!_comparer.Equals(existing, memory))
                    throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.StateConflict, "Memory payload is immutable after creation.");
                return ValueTask.CompletedTask;
            }

            if (memory.Status != AgentMemoryStatus.Active
                || memory.IsAuthoritative
                || memory.SupersedesMemoryId is not null
                || memory.SupersededByMemoryId is not null)
            {
                throw new AgentMemoryOperationException(
                    AgentMemoryOperationFailureCode.InvalidLifecycleState,
                    "A new Memory must be Active, non-authoritative, and unlinked.");
            }

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
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = GetCandidateUnsafe(tenantId, plan.Candidate.CandidateId)
                ?? throw Unavailable("Candidate is unavailable.");
            if (_memories.ContainsKey((tenantId, plan.NewMemoryId)))
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Memory identity conflicts.");

            var mutation = _stateMachine.PreparePromote(tenantId, candidate, plan);
            _memories[(tenantId, mutation.Memory.MemoryId)] = mutation.Memory.Snapshot();
            _candidates[(tenantId, mutation.Candidate.CandidateId)] = mutation.Candidate.Snapshot();
            return ValueTask.FromResult(mutation.Memory.Snapshot());
        }
    }

    public ValueTask RejectAsync(string tenantId, AgentMemoryCandidateExpectation expectation, AgentMemoryOperationRequest operation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = GetCandidateUnsafe(tenantId, expectation.CandidateId)
                ?? throw Unavailable("Candidate is unavailable.");

            var mutation = _stateMachine.PrepareReject(tenantId, candidate, expectation);
            _candidates[(tenantId, mutation.Candidate.CandidateId)] = mutation.Candidate.Snapshot();
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, AgentMemorySupersessionPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = GetMemoryUnsafe(tenantId, plan.TargetMemory.MemoryId)
                ?? throw Unavailable("Target Memory is unavailable.");
            var replacement = GetCandidateUnsafe(tenantId, plan.ReplacementCandidate.CandidateId)
                ?? throw Unavailable("Replacement Candidate is unavailable.");
            if (_memories.ContainsKey((tenantId, plan.NewMemoryId)))
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Memory identity conflicts.");

            var mutation = _stateMachine.PrepareSupersede(tenantId, existing, replacement, plan);
            _memories[(tenantId, mutation.SupersededMemory.MemoryId)] = mutation.SupersededMemory.Snapshot();
            _memories[(tenantId, mutation.SupersedingMemory.MemoryId)] = mutation.SupersedingMemory.Snapshot();
            _candidates[(tenantId, mutation.ReplacementCandidate.CandidateId)] = mutation.ReplacementCandidate.Snapshot();
            return ValueTask.FromResult(mutation.SupersedingMemory.Snapshot());
        }
    }

    public ValueTask<AgentMemoryItem> ArchiveAsync(string tenantId, AgentMemoryItemExpectation memory, AgentMemoryOperationRequest operation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = GetMemoryUnsafe(tenantId, memory.MemoryId)
                ?? throw Unavailable("Memory is unavailable.");

            var mutation = _stateMachine.PrepareArchive(tenantId, existing, memory);
            _memories[(tenantId, mutation.Memory.MemoryId)] = mutation.Memory.Snapshot();
            return ValueTask.FromResult(mutation.Memory.Snapshot());
        }
    }

    private AgentMemoryCandidate? GetCandidateUnsafe(string tenantId, string id)
        => _candidates.TryGetValue((tenantId, id), out var candidate) ? candidate : null;

    private AgentMemoryItem? GetMemoryUnsafe(string tenantId, string id)
        => _memories.TryGetValue((tenantId, id), out var memory) ? memory : null;

    /// <summary>Fails closed when curation is attempted without a hash projector,
    /// preserving the legacy constructor contract (base Store operations do not
    /// require state hashes).</summary>
    private sealed class UnavailableStateHashProjector : IAgentMemoryStateHashProjector
    {
        public CanonicalHash ComputeCandidateStateHash(AgentMemoryCandidate candidate)
            => throw new AgentMemoryOperationException(
                AgentMemoryOperationFailureCode.Unknown,
                "State hash projector is unavailable.");

        public CanonicalHash ComputeMemoryStateHash(AgentMemoryItem memory)
            => throw new AgentMemoryOperationException(
                AgentMemoryOperationFailureCode.Unknown,
                "State hash projector is unavailable.");
    }

    private static AgentMemoryOperationException Unavailable(string message)
        => new(AgentMemoryOperationFailureCode.ResourceUnavailable, message);

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
