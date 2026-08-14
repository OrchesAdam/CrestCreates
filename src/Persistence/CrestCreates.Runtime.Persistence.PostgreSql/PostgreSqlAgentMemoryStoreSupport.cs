using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Snapshot.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using Npgsql;
using NpgsqlTypes;

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
    {
        try
        {
            return JsonSerializer.Deserialize(payload, typeInfo)
                   ?? throw new RuntimePersistenceContractException(
                       RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                       "PostgreSQL Agent Memory persistence returned an invalid JSON payload.");
        }
        catch (JsonException)
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                "PostgreSQL Agent Memory persistence returned malformed JSON.");
        }
    }

    /// <summary>Deserializes and snapshots a persisted JSON row, translating
    /// every invalid persisted shape — malformed JSON, missing required
    /// members, wrong types, null collections — into the frozen
    /// <see cref="RuntimePersistenceContractErrorCode.PersistedInvariantViolation"/>
    /// failure taxonomy instead of leaking JsonException or NullReferenceException.</summary>
    public static T DeserializeSnapshot<T>(string payload, JsonTypeInfo<T> typeInfo)
        where T : ISnapshotable<T>
    {
        T value;
        try
        {
            value = Deserialize(payload, typeInfo);
            return value.Snapshot();
        }
        catch (RuntimePersistenceContractException)
        {
            throw;
        }
        catch (Exception exception) when (exception is NullReferenceException or InvalidOperationException or ArgumentException)
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                "PostgreSQL Agent Memory persistence returned an invalid snapshot shape.");
        }
    }

    /// <summary>Normalizes a promoted timestamp for durable storage: UTC
    /// instant with microsecond precision, matching PostgreSQL timestamptz.
    /// Applied before JSON serialization and state-hash computation so the
    /// JSON snapshot, structured column, and hash all agree on one value.</summary>
    public static DateTimeOffset NormalizePromotedAt(DateTimeOffset value)
    {
        var utcTicks = value.ToUniversalTime().UtcTicks;
        var microsecondTicks = TimeSpan.TicksPerMicrosecond;
        var truncated = utcTicks - (utcTicks % microsecondTicks);
        return new DateTimeOffset(truncated, TimeSpan.Zero);
    }

    public static void AddJsonParameter(NpgsqlCommand command, string name, string value)
        => command.Parameters.Add(name, NpgsqlDbType.Jsonb).Value = value;

    public static RuntimePersistenceContractException Invariant(string message)
        => new(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, message);

    public static AgentMemoryOperationException DomainFailure(AgentMemoryOperationFailureCode code, string message)
        => new(code, message);
}
