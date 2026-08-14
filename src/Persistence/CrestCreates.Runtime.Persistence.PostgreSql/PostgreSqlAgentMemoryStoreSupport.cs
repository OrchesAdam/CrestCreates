using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Shared durable-row helpers for the Agent Memory Stores: table names,
/// JSON serialize/deserialize through exact generated type info, persisted
/// invariant validation, and bounded error construction.
/// </summary>
public static class PostgreSqlAgentMemoryStoreSupport
{
    public const int StateContractVersion = 1;

    public static string Table(PostgreSqlRuntimePersistenceOptions options, string table)
        => $"\"{options.Schema}\".\"{table}\"";

    public static string Serialize<T>(T value, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(value, typeInfo);

    public static T Deserialize<T>(string payload, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Deserialize(payload, typeInfo)
           ?? throw new RuntimePersistenceContractException(
               RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
               "PostgreSQL Agent Memory persistence returned an invalid JSON payload.");

    public static RuntimePersistenceContractException Invariant(string message)
        => new(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, message);

    public static AgentMemoryOperationException DomainFailure(AgentMemoryOperationFailureCode code, string message)
        => new(code, message);
}
