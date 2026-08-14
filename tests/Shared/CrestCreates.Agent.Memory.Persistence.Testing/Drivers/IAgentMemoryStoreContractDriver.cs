using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Persistence.Testing.Drivers;

/// <summary>
/// Provider-neutral driver over one Agent Memory persistence backend. Exposes
/// only the existing Store contracts, lifecycle/setup utilities, and
/// provider-neutral plan preparation. Never exposes Npgsql, SQL, schema names,
/// concrete Stores, or provider-specific exceptions.
/// </summary>
public interface IAgentMemoryStoreContractDriver : IAsyncDisposable
{
    IAgentConversationStore ConversationStore { get; }
    IAgentTaskHistoryStore TaskStore { get; }
    IAgentCompressedContextStore ContextStore { get; }
    IAgentMemoryStore MemoryStore { get; }

    /// <summary>The sanitizer configured for this backend. The shared contract
    /// cases require it to reject content containing
    /// <see cref="AgentMemoryPersistenceContractMarkers.RejectedContentSentinel"/>.</summary>
    IAgentMemoryContentSanitizer Sanitizer { get; }

    ValueTask ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a fresh reader against the same durable state. Optional
    /// durability capability: implementations that cannot observe the same state
    /// through a second provider must return a reader over the same stores.</summary>
    ValueTask<IAgentMemoryStoreContractDriver> CreateFreshReaderAsync(CancellationToken cancellationToken = default);

    AgentMemoryCandidateExpectation PrepareCandidateExpectation(AgentMemoryCandidate candidate);
    AgentMemoryItemExpectation PrepareMemoryExpectation(AgentMemoryItem memory);
    AgentMemoryPromotionPlan PreparePromotionPlan(AgentMemoryCandidate candidate, string newMemoryId, AgentMemoryOperationRequest operation);
    AgentMemorySupersessionPlan PrepareSupersessionPlan(AgentMemoryItem targetMemory, AgentMemoryCandidate replacementCandidate, string newMemoryId, AgentMemoryOperationRequest operation);
    AgentMemoryItem ProjectPromotedMemory(AgentMemoryCandidate candidate, string newMemoryId, AgentMemoryOperationRequest operation);
}

/// <summary>
/// Provider-neutral markers shared by every concrete runner. The runners'
/// sanitizers must reject content containing
/// <see cref="RejectedContentSentinel"/> so the shared sanitization cases can
/// observe rejection deterministically.
/// </summary>
public static class AgentMemoryPersistenceContractMarkers
{
    public const string RejectedContentSentinel = "###CRESTCREATES_REJECTED_CONTENT###";
}

public sealed record AgentMemoryRevisionObservation(
    AgentMemoryArtifactKind ArtifactKind,
    string TenantId,
    string ArtifactId,
    long Revision);

public enum AgentMemoryArtifactKind
{
    Conversation = 0,
    Task = 1,
    CompressedContext = 2,
    Candidate = 3,
    Memory = 4
}
