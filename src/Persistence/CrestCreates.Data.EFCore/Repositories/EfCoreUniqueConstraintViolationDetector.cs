using System;

namespace CrestCreates.Data.EFCore.Repositories;

/// <summary>
/// Recognizes provider unique-constraint exceptions without taking a compile-time dependency on
/// every database driver. Provider type and property names are the only stable cross-provider
/// signal available at this layer; non-unique failures always return false. When a table name is
/// supplied, provider details that expose the failing relation must agree with it so a conflict
/// on a different tracked entity is never reported as this repository's natural-key conflict.
/// </summary>
internal static class EfCoreUniqueConstraintViolationDetector
{
    public static bool IsUniqueConstraintViolation(Exception exception, string? tableName = null)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (IsKnownUniqueViolation(current, tableName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKnownUniqueViolation(Exception exception, string? tableName)
    {
        return exception.GetType().FullName switch
        {
            "Npgsql.PostgresException" => ReadString(exception, "SqlState") == "23505" &&
                                          MatchesNpgsqlTable(exception, tableName),
            "Microsoft.Data.SqlClient.SqlException" => ReadInt(exception, "Number") is 2601 or 2627 &&
                                                        ContainsTableName(exception.Message, tableName),
            "Microsoft.Data.Sqlite.SqliteException" => IsSqliteUniqueViolation(exception) &&
                                                       ContainsTableName(exception.Message, tableName),
            "MySql.Data.MySqlClient.MySqlException" or "MySqlConnector.MySqlException" =>
                ReadInt(exception, "Number") == 1062,
            _ => false
        };
    }

    private static bool MatchesNpgsqlTable(Exception exception, string? tableName)
    {
        if (tableName is null)
        {
            return true;
        }

        var reported = ReadString(exception, "TableName");
        return string.IsNullOrEmpty(reported) ||
               string.Equals(reported, tableName, StringComparison.Ordinal);
    }

    private static bool ContainsTableName(string? message, string? tableName)
    {
        return tableName is null ||
               (message is not null && message.Contains(tableName, StringComparison.Ordinal));
    }

    private static bool IsSqliteUniqueViolation(Exception exception)
    {
        return ReadInt(exception, "SqliteErrorCode") == 19 &&
               ReadInt(exception, "SqliteExtendedErrorCode") is 1555 or 2067;
    }

    private static string? ReadString(Exception exception, string propertyName)
    {
        return exception.GetType().GetProperty(propertyName)?.GetValue(exception) as string;
    }

    private static int? ReadInt(Exception exception, string propertyName)
    {
        return exception.GetType().GetProperty(propertyName)?.GetValue(exception) as int?;
    }
}
