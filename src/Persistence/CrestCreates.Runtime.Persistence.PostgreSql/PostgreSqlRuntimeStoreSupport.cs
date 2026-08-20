using System.Text.Json;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal static class PostgreSqlRuntimeStoreSupport
{
    public static string Table(PostgreSqlRuntimePersistenceOptions options, string table)
        => $"\"{options.Schema}\".\"{table}\"";

    public static string ScopeKind(string? tenantId) => tenantId is null ? "host" : "tenant";

    public static string TenantValue(string? tenantId) => tenantId ?? string.Empty;

    public static void AddKey(NpgsqlCommand command, RuntimeInstanceKey key, string prefix = "")
    {
        key.EnsureValid();
        command.Parameters.AddWithValue($"{prefix}scope", ScopeKind(key.TenantId));
        command.Parameters.AddWithValue($"{prefix}tenant", TenantValue(key.TenantId));
        command.Parameters.AddWithValue($"{prefix}id", key.InstanceId);
    }

    public static void AddScope(NpgsqlCommand command, RuntimeTenantScope scope, string prefix = "")
    {
        scope.EnsureValid();
        command.Parameters.AddWithValue($"{prefix}scope", ScopeKind(scope.TenantId));
        command.Parameters.AddWithValue($"{prefix}tenant", TenantValue(scope.TenantId));
    }

    public static void AddJson(NpgsqlCommand command, string name, string value)
        => command.Parameters.Add(name, NpgsqlDbType.Jsonb).Value = value;

    public static NpgsqlCommand CreateCommand(
        PostgreSqlRuntimeSession session,
        PostgreSqlRuntimePersistenceOptions options,
        string sql)
    {
        var command = new NpgsqlCommand(sql, session.Connection, session.Transaction)
        {
            CommandTimeout = options.CommandTimeoutSeconds
        };
        return command;
    }

    public static string Serialize<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(value, typeInfo);

    public static T Deserialize<T>(string payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(payload, typeInfo)
                ?? throw new RuntimePersistenceContractException(
                    RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                    "PostgreSQL Runtime persistence returned an invalid JSON payload.");
        }
        catch (JsonException)
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                "PostgreSQL Runtime persistence returned malformed JSON.");
        }
    }

    public static RuntimePersistenceContractException Correlation(string message)
        => new(RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict, message);

    public static bool IsUniqueViolation(PostgresException exception, string constraint)
        => exception.SqlState == PostgresErrorCodes.UniqueViolation
            && string.Equals(exception.ConstraintName, constraint, StringComparison.Ordinal);

    /// <summary>
    /// Semantic JSON equality. PostgreSQL normalizes jsonb values (sorted keys,
    /// no insignificant whitespace), so a stored jsonb read back as text never
    /// string-matches a freshly serialized payload even when the documents are
    /// equivalent. Compare parsed documents instead.
    /// </summary>
    public static bool JsonEquals(string? left, string? right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
            return true;
        if (left is null || right is null)
            return false;
        try
        {
            using var leftDocument = JsonDocument.Parse(left);
            using var rightDocument = JsonDocument.Parse(right);
            return JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static RuntimePersistenceContractException TranslateForeignKeyViolation(PostgresException exception)
    {
        var code = exception.ConstraintName switch
        {
            "fk_workflow_waiting_task_reciprocal" => RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
            "fk_receipt_human_task_reciprocal" => RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
            _ => RuntimePersistenceContractErrorCode.PersistedInvariantViolation
        };

        return new RuntimePersistenceContractException(
            code,
            "The persisted Runtime correlation violates a required invariant.");
    }
}
