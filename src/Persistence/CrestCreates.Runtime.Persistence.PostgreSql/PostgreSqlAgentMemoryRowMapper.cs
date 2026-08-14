using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.Memory.Abstractions;
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

        var snapshot = PostgreSqlAgentMemoryStoreSupport.Deserialize(stateJson, typeInfo).Snapshot();
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

        var snapshot = PostgreSqlAgentMemoryStoreSupport.Deserialize(stateJson, typeInfo).Snapshot();
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

        var snapshot = PostgreSqlAgentMemoryStoreSupport.Deserialize(stateJson, typeInfo).Snapshot();
        if (!string.Equals(snapshot.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(snapshot.ContextId, contextId, StringComparison.Ordinal))
        {
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Context identity columns disagree with the JSON snapshot.");
        }
        return snapshot;
    }
}
