using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal static class PostgreSqlControlPlaneReferenceDataStoreSupport
{
    internal const int StateContractVersion = 1;

    internal static string Table(PostgreSqlRuntimePersistenceOptions options, string table)
        => $"\"{options.Schema}\".\"{table}\"";

    internal static string NormalizeReadableTimestamp(DateTimeOffset value)
    {
        var utcTicks = value.UtcTicks;
        var truncated = utcTicks - (utcTicks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(truncated, TimeSpan.Zero).ToString("O");
    }

    internal static long UtcTicks(DateTimeOffset value) => value.UtcTicks;

    internal static string TenantScope(string? tenantId) => tenantId is null ? "global" : "tenant";
    internal static string TenantValue(string? tenantId) => tenantId ?? "";

    internal static NpgsqlCommand CreateReadCommand(
        NpgsqlConnection connection,
        PostgreSqlRuntimePersistenceOptions options,
        string sql)
    {
        return new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = options.CommandTimeoutSeconds
        };
    }

    internal static NpgsqlCommand CreateWriteCommand(
        PostgreSqlRuntimeSession session,
        PostgreSqlRuntimePersistenceOptions options,
        string sql)
    {
        return new NpgsqlCommand(sql, session.Connection, session.Transaction)
        {
            CommandTimeout = options.CommandTimeoutSeconds
        };
    }

    internal static RuntimePersistenceContractException PersistedInvariant(string message)
        => new(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, message);

    internal static async Task<T> ExecuteReadAsync<T>(
        NpgsqlDataSource dataSource,
        Func<NpgsqlConnection, CancellationToken, ValueTask<T>> work,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            return await work(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimePersistenceException)
        {
            throw;
        }
        catch (NpgsqlException)
        {
            throw new RuntimePersistenceUnavailableException("PostgreSQL Runtime persistence is unavailable.");
        }
    }
}
