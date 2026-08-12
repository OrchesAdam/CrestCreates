using CrestCreates.Agent.Memory.Abstractions.Accountability;

namespace CrestCreates.Agent.Memory.Abstractions;

public interface IAgentConversationStore
{
    ValueTask SaveConversationAsync(AgentConversationRecord conversation, CancellationToken cancellationToken = default);
    ValueTask<AgentConversationRecord?> GetConversationAsync(string tenantId, string conversationId, CancellationToken cancellationToken = default);
}

public interface IAgentTaskHistoryStore
{
    ValueTask SaveTaskAsync(AgentTaskRecord task, CancellationToken cancellationToken = default);
    ValueTask<AgentTaskRecord?> GetTaskAsync(string tenantId, string taskId, CancellationToken cancellationToken = default);
    ValueTask AppendEventAsync(string tenantId, string taskId, AgentTaskEvent taskEvent, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AgentTaskRecord>> ListTasksAsync(string tenantId, CancellationToken cancellationToken = default);
}

public interface IAgentCompressedContextStore
{
    ValueTask SaveCompressedContextAsync(AgentCompressedContext context, CancellationToken cancellationToken = default);
    ValueTask CreateCompressedContextAsync(AgentCompressedContext context, CancellationToken cancellationToken = default);
    ValueTask<AgentCompressedContext?> GetCompressedContextAsync(string tenantId, string contextId, CancellationToken cancellationToken = default);
    ValueTask<AgentCompressedContextBlock?> GetCompressedContextBlockAsync(string tenantId, string blockId, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryStore
{
    /// <summary>Creates a candidate and rejects every existing identity.</summary>
    ValueTask SaveCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default);
    ValueTask CreateCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default);
    ValueTask CreateCandidatesAsync(IReadOnlyList<AgentMemoryCandidate> candidates, CancellationToken cancellationToken = default);
    ValueTask TransitionCandidateStatusAsync(
        string tenantId,
        string candidateId,
        AgentMemoryStatus expectedStatus,
        AgentMemoryStatus newStatus,
        CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryCandidate?> GetCandidateAsync(string tenantId, string candidateId, CancellationToken cancellationToken = default);
    ValueTask SaveMemoryAsync(AgentMemoryItem memory, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryItem?> GetMemoryAsync(string tenantId, string memoryId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AgentMemoryItem>> ListMemoriesAsync(AgentMemoryQuery query, CancellationToken cancellationToken = default);
}

public enum AgentMemoryCurationOutcomeGuarantee
{
    Unknown = 0,
    ConfirmedAtomic = 1
}

public interface IAgentMemoryStoreCapabilities
{
    AgentMemoryCurationOutcomeGuarantee CurationOutcomeGuarantee { get; }
}

/// <summary>
/// Store-owned conditional transitions. Implementations must perform the
/// expectation check and every lifecycle write in one provider primitive.
/// </summary>
public interface IAgentMemoryConditionalCurationStore
{
    ValueTask<AgentMemoryItem> PromoteAsync(
        string tenantId,
        AgentMemoryPromotionPlan plan,
        CancellationToken cancellationToken = default);

    ValueTask RejectAsync(
        string tenantId,
        AgentMemoryCandidateExpectation candidate,
        AgentMemoryOperationRequest operation,
        CancellationToken cancellationToken = default);

    ValueTask<AgentMemoryItem> SupersedeAsync(
        string tenantId,
        AgentMemorySupersessionPlan plan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transitions an Active or Superseded memory to Archived after
    /// verifying the caller's expectation (state hash) matches the current item.
    /// Cancellation must be observed before any lifecycle write.
    /// </summary>
    ValueTask<AgentMemoryItem> ArchiveAsync(
        string tenantId,
        AgentMemoryItemExpectation memory,
        AgentMemoryOperationRequest operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker surfaced by AddAgentMemoryCuration. Read-only runtimes that never
/// curate (no promotion/archive) must not register it; the curation composition
/// validator fails closed when the marker is present but the store is not a
/// conditional curation store.
/// </summary>
public interface IAgentMemoryFormalCurationMarker
{
}

public interface IAgentMemoryCurationServiceCapabilities
{
    AgentMemoryCurationOutcomeGuarantee OutcomeGuarantee { get; }
}

public interface IAgentMemoryContentSanitizer
{
    SanitizedAgentContent Sanitize(string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs);
}

/// <summary>
/// Allocates framework-owned, opaque identities for persisted memory artifacts.
/// Provider labels and source identifiers must never become store keys.
/// </summary>
public interface IAgentMemoryArtifactIdGenerator
{
    string CreateContextId();
    string CreateBlockId();
    string CreateCandidateId();
    string CreateMemoryId();
}

/// <summary>
/// Allocates the stable identity pair of one admitted Memory operation exactly once.
/// The factory is invoked by first-party adapters at operation admission; producers
/// never call it, and republication reuses the snapshot pair.
/// </summary>
public interface IAgentMemoryOperationIdentityFactory
{
    AgentMemoryOperationIdentity Create();
}

public interface IAgentContextCompressor
{
    ValueTask<AgentCompressedContext> CompressConversationAsync(AgentConversationRecord conversation, CancellationToken cancellationToken = default);
    ValueTask<AgentCompressedContext> CompressTaskAsync(AgentTaskRecord task, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryExtractor
{
    ValueTask<IReadOnlyList<AgentMemoryCandidate>> ExtractCandidatesAsync(AgentCompressedContext context, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryPromotionService
{
    ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, AgentMemoryPromotionPlan plan, CancellationToken cancellationToken = default);
    ValueTask RejectAsync(string tenantId, AgentMemoryCandidateExpectation candidate, AgentMemoryOperationRequest operation, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, AgentMemorySupersessionPlan plan, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, string candidateId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, string candidateId, string newMemoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
    ValueTask RejectAsync(string tenantId, string candidateId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, string memoryId, AgentMemoryCandidate replacement, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, string memoryId, string replacementCandidateId, string newMemoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
    ValueTask ArchiveAsync(string tenantId, string memoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryRetriever
{
    ValueTask<AgentMemoryPack> RecallAsync(AgentMemoryQuery query, CancellationToken cancellationToken = default);
}

public interface IAgentContextSourceExpander
{
    ValueTask<AgentSourceExpansionResult> ExpandAsync(AgentContextSourceRef sourceRef, CancellationToken cancellationToken = default);
}

public interface IAgentAuthoringContextBuilder
{
    ValueTask<AgentAuthoringContext> BuildAsync(
        AgentAuthoringRequest request,
        MetadataContextPack metadataContextPack,
        AgentMemoryPack memoryPack,
        CancellationToken cancellationToken = default);
}
