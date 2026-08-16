using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Maps durable Agent Memory rows to detached snapshots after persisted
/// invariant validation. All validation failures surface as
/// <see cref="RuntimePersistenceContractException(PersistedInvariantViolation)"/>.
/// </summary>
public static class PostgreSqlAgentMemoryRowMapper
{
    public static AgentConversationRecord MapConversation(
        string tenantId,
        string conversationId,
        long revision,
        int stateContractVersion,
        string stateJson,
        JsonTypeInfo<AgentConversationRecord> typeInfo)
    {
        if (stateContractVersion != PostgreSqlAgentMemoryStoreSupport.StateContractVersion)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Conversation state_contract_version is unsupported.");
        if (revision <= 0)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Conversation revision must be positive.");

        var snapshot = PostgreSqlAgentMemoryStoreSupport.DeserializeSnapshot(stateJson, typeInfo);
        if (!string.Equals(snapshot.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(snapshot.ConversationId, conversationId, StringComparison.Ordinal))
        {
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Conversation identity columns disagree with the JSON snapshot.");
        }
        return snapshot;
    }

    public static AgentTaskRecord MapTask(
        string tenantId,
        string taskId,
        long revision,
        int stateContractVersion,
        string stateJson,
        JsonTypeInfo<AgentTaskRecord> typeInfo)
    {
        if (stateContractVersion != PostgreSqlAgentMemoryStoreSupport.StateContractVersion)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Task state_contract_version is unsupported.");
        if (revision <= 0)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Task revision must be positive.");

        var snapshot = PostgreSqlAgentMemoryStoreSupport.DeserializeSnapshot(stateJson, typeInfo);
        if (!string.Equals(snapshot.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(snapshot.TaskId, taskId, StringComparison.Ordinal))
        {
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Task identity columns disagree with the JSON snapshot.");
        }
        return snapshot;
    }

    public static AgentCompressedContext MapContext(
        string tenantId,
        string contextId,
        long revision,
        int stateContractVersion,
        string stateJson,
        JsonTypeInfo<AgentCompressedContext> typeInfo)
    {
        if (stateContractVersion != PostgreSqlAgentMemoryStoreSupport.StateContractVersion)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Context state_contract_version is unsupported.");
        if (revision <= 0)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Context revision must be positive.");

        var snapshot = PostgreSqlAgentMemoryStoreSupport.DeserializeSnapshot(stateJson, typeInfo);
        if (!string.Equals(snapshot.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(snapshot.ContextId, contextId, StringComparison.Ordinal))
        {
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Context identity columns disagree with the JSON snapshot.");
        }
        return snapshot;
    }

    public static AgentMemoryCandidate MapCandidate(
        string tenantId,
        string candidateId,
        long revision,
        int status,
        int kind,
        string canonicalContentHash,
        string stateHash,
        int stateContractVersion,
        string stateJson,
        JsonTypeInfo<AgentMemoryCandidate> typeInfo,
        IAgentMemoryStateHashProjector stateHashes)
    {
        if (stateContractVersion != PostgreSqlAgentMemoryStoreSupport.StateContractVersion)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Candidate state_contract_version is unsupported.");
        if (revision <= 0)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Candidate revision must be positive.");
        if (!Enum.IsDefined(typeof(AgentMemoryStatus), status) || !Enum.IsDefined(typeof(AgentMemoryKind), kind))
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Candidate enum columns are undefined.");

        var snapshot = PostgreSqlAgentMemoryStoreSupport.DeserializeSnapshot(stateJson, typeInfo);
        if (!string.Equals(snapshot.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(snapshot.CandidateId, candidateId, StringComparison.Ordinal))
        {
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Candidate identity columns disagree with the JSON snapshot.");
        }
        if ((int)snapshot.Status != status || (int)snapshot.Kind != kind)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Candidate enum columns disagree with the JSON snapshot.");
        if (snapshot.CanonicalContentHash is null)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Candidate canonical content hash is required.");
        if (!string.Equals(snapshot.CanonicalContentHash.Value, canonicalContentHash, StringComparison.Ordinal))
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Candidate canonical content hash disagrees with the JSON snapshot.");
        if (!string.Equals(stateHashes.ComputeCandidateStateHash(snapshot).Value, stateHash, StringComparison.Ordinal))
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Candidate state hash disagrees with the JSON snapshot.");
        return snapshot;
    }

    public static AgentMemoryItem MapMemory(
        string tenantId,
        string memoryId,
        long revision,
        int status,
        int kind,
        int confidence,
        DateTimeOffset promotedAt,
        string canonicalContentHash,
        string stateHash,
        string? supersedesMemoryId,
        string? supersededByMemoryId,
        int stateContractVersion,
        string stateJson,
        JsonTypeInfo<AgentMemoryItem> typeInfo,
        IAgentMemoryStateHashProjector stateHashes)
    {
        if (stateContractVersion != PostgreSqlAgentMemoryStoreSupport.StateContractVersion)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Memory state_contract_version is unsupported.");
        if (revision <= 0)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Memory revision must be positive.");
        if (!Enum.IsDefined(typeof(AgentMemoryStatus), status)
            || !Enum.IsDefined(typeof(AgentMemoryKind), kind)
            || !Enum.IsDefined(typeof(AgentMemoryConfidence), confidence))
        {
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Memory enum columns are undefined.");
        }

        var snapshot = PostgreSqlAgentMemoryStoreSupport.DeserializeSnapshot(stateJson, typeInfo);
        if (!string.Equals(snapshot.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(snapshot.MemoryId, memoryId, StringComparison.Ordinal))
        {
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Memory identity columns disagree with the JSON snapshot.");
        }
        if ((int)snapshot.Status != status
            || (int)snapshot.Kind != kind
            || (int)snapshot.Confidence != confidence
            || PostgreSqlAgentMemoryStoreSupport.NormalizePromotedAt(snapshot.PromotedAt) != promotedAt)
        {
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Memory structured columns disagree with the JSON snapshot.");
        }
        if (snapshot.CanonicalContentHash is null)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Memory canonical content hash is required.");
        if (!string.Equals(snapshot.CanonicalContentHash.Value, canonicalContentHash, StringComparison.Ordinal))
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Memory canonical content hash disagrees with the JSON snapshot.");
        if (!string.Equals(snapshot.SupersedesMemoryId, supersedesMemoryId, StringComparison.Ordinal)
            || !string.Equals(snapshot.SupersededByMemoryId, supersededByMemoryId, StringComparison.Ordinal))
        {
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Memory graph columns disagree with the JSON snapshot.");
        }
        if (!string.Equals(stateHashes.ComputeMemoryStateHash(snapshot).Value, stateHash, StringComparison.Ordinal))
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Memory state hash disagrees with the JSON snapshot.");
        return snapshot;
    }

    public static AgentCompressedContextBlock MapContextBlock(
        string tenantId,
        string blockId,
        string contextId,
        int ordinal,
        int stateContractVersion,
        string blockJson,
        JsonTypeInfo<AgentCompressedContextBlock> typeInfo)
    {
        if (stateContractVersion != PostgreSqlAgentMemoryStoreSupport.StateContractVersion)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Block state_contract_version is unsupported.");
        if (ordinal < 0)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Block ordinal must be non-negative.");

        var snapshot = PostgreSqlAgentMemoryStoreSupport.DeserializeSnapshot(blockJson, typeInfo);
        if (!string.Equals(snapshot.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(snapshot.BlockId, blockId, StringComparison.Ordinal))
        {
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Block identity columns disagree with the JSON snapshot.");
        }
        return snapshot;
    }
}
