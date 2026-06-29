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
    ValueTask<AgentCompressedContext?> GetCompressedContextAsync(string tenantId, string contextId, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryStore
{
    ValueTask SaveCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryCandidate?> GetCandidateAsync(string tenantId, string candidateId, CancellationToken cancellationToken = default);
    ValueTask SaveMemoryAsync(AgentMemoryItem memory, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryItem?> GetMemoryAsync(string tenantId, string memoryId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AgentMemoryItem>> ListMemoriesAsync(AgentMemoryQuery query, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryContentSanitizer
{
    SanitizedAgentContent Sanitize(string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs);
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
    ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, string candidateId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
    ValueTask RejectAsync(string tenantId, string candidateId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, string memoryId, AgentMemoryCandidate replacement, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default);
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
    ValueTask<AgentAuthoringContext> BuildAsync(AgentAuthoringRequest request, MetadataContextPack metadataContextPack, CancellationToken cancellationToken = default);
}
