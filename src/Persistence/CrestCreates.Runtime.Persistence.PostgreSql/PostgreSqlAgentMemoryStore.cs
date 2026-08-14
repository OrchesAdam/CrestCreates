using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;
using CrestCreates.Agent.Memory.Abstractions.Curation;
using CrestCreates.Agent.Memory.Abstractions.Persistence;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Durable Candidate/Memory store participant implementing the base Store
/// contract plus conditional curation and capability surfaces on the same
/// instance. Conditional curation owns a provider-level top-level COMMIT
/// boundary; the capability guarantee becomes ConfirmedAtomic only after all
/// four primitives are implemented atomically.
/// </summary>
internal sealed class PostgreSqlAgentMemoryStore : IAgentMemoryStore, IAgentMemoryStoreCapabilities, IAgentMemoryConditionalCurationStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly PostgreSqlAgentMemoryLockManager _lockManager;
    private readonly IAgentMemoryStateHashProjector _stateHashes;
    private readonly IAgentMemoryCurationStateMachine _stateMachine;
    private readonly IAgentMemoryPersistenceComparer _comparer;

    public PostgreSqlAgentMemoryStore(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator,
        PostgreSqlAgentMemoryLockManager lockManager,
        IAgentMemoryStateHashProjector stateHashes,
        IAgentMemoryCurationStateMachine stateMachine,
        IAgentMemoryPersistenceComparer comparer)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
        _stateHashes = stateHashes ?? throw new ArgumentNullException(nameof(stateHashes));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
    }

    /// <summary>Truthful capability: promoted to ConfirmedAtomic only after all
    /// four formal primitives are implemented atomically (Slice 8).</summary>
    public AgentMemoryCurationOutcomeGuarantee CurationOutcomeGuarantee
        => AgentMemoryCurationOutcomeGuarantee.Unknown;

    public ValueTask SaveCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => SaveCandidateCoreAsync(candidate, ct), cancellationToken);

    public ValueTask CreateCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default)
        => CreateCandidatesAsync([candidate], cancellationToken);

    public ValueTask CreateCandidatesAsync(IReadOnlyList<AgentMemoryCandidate> candidates, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => CreateCandidatesCoreAsync(candidates, ct), cancellationToken);

    public ValueTask TransitionCandidateStatusAsync(
        string tenantId,
        string candidateId,
        AgentMemoryStatus expectedStatus,
        AgentMemoryStatus newStatus,
        CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => TransitionCandidateStatusCoreAsync(tenantId, candidateId, expectedStatus, newStatus, ct), cancellationToken);

    public ValueTask<AgentMemoryCandidate?> GetCandidateAsync(string tenantId, string candidateId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetCandidateCoreAsync(tenantId, candidateId, ct), cancellationToken);

    public ValueTask SaveMemoryAsync(AgentMemoryItem memory, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => SaveMemoryCoreAsync(memory, ct), cancellationToken);

    public ValueTask<AgentMemoryItem?> GetMemoryAsync(string tenantId, string memoryId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetMemoryCoreAsync(tenantId, memoryId, ct), cancellationToken);

    public ValueTask<IReadOnlyList<AgentMemoryItem>> ListMemoriesAsync(AgentMemoryQuery query, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => ListMemoriesCoreAsync(query, ct), cancellationToken);

    public ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, AgentMemoryPromotionPlan plan, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteTopLevelAsync(ct => PromoteCoreAsync(tenantId, plan, ct), cancellationToken);

    public ValueTask RejectAsync(string tenantId, AgentMemoryCandidateExpectation candidate, AgentMemoryOperationRequest operation, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteTopLevelAsync(ct => RejectCoreAsync(tenantId, candidate, operation, ct), cancellationToken);

    public ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, AgentMemorySupersessionPlan plan, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteTopLevelAsync(ct => SupersedeCoreAsync(tenantId, plan, ct), cancellationToken);

    public ValueTask<AgentMemoryItem> ArchiveAsync(string tenantId, AgentMemoryItemExpectation memory, AgentMemoryOperationRequest operation, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteTopLevelAsync(ct => ArchiveCoreAsync(tenantId, memory, operation, ct), cancellationToken);

    private async ValueTask SaveCandidateCoreAsync(AgentMemoryCandidate candidate, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask CreateCandidatesCoreAsync(IReadOnlyList<AgentMemoryCandidate> candidates, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask TransitionCandidateStatusCoreAsync(
        string tenantId, string candidateId, AgentMemoryStatus expectedStatus, AgentMemoryStatus newStatus, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask<AgentMemoryCandidate?> GetCandidateCoreAsync(string tenantId, string candidateId, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask SaveMemoryCoreAsync(AgentMemoryItem memory, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask<AgentMemoryItem?> GetMemoryCoreAsync(string tenantId, string memoryId, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask<IReadOnlyList<AgentMemoryItem>> ListMemoriesCoreAsync(AgentMemoryQuery query, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask<AgentMemoryItem> PromoteCoreAsync(string tenantId, AgentMemoryPromotionPlan plan, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask RejectCoreAsync(string tenantId, AgentMemoryCandidateExpectation candidate, AgentMemoryOperationRequest operation, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask<AgentMemoryItem> SupersedeCoreAsync(string tenantId, AgentMemorySupersessionPlan plan, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");

    private async ValueTask<AgentMemoryItem> ArchiveCoreAsync(string tenantId, AgentMemoryItemExpectation memory, AgentMemoryOperationRequest operation, CancellationToken ct)
        => throw new NotSupportedException("PostgreSQL Agent Memory Store activation is scheduled for a later Slice.");
}
