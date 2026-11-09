using System;

namespace CrestCreates.Data.EFCore.Repositories;

/// <summary>
/// Recognizes provider unique-constraint exceptions without taking a compile-time dependency on
/// every database driver. Provider type and property names are the only stable cross-provider
/// signal available at this layer; non-unique failures always return false.
/// </summary>
internal static class EfCoreUniqueConstraintViolationDetector
{
    public static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (IsKnownUniqueViolation(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKnownUniqueViolation(Exception exception)
    {
        return exception.GetType().FullName switch
        {
            "Npgsql.PostgresException" => ReadString(exception, "SqlState") == "23505",
            "Microsoft.Data.SqlClient.SqlException" => ReadInt(exception, "Number") is 2601 or 2627,
            "Microsoft.Data.Sqlite.SqliteException" => IsSqliteUniqueViolation(exception),
            "MySql.Data.MySqlClient.MySqlException" or "MySqlConnector.MySqlException" =>
                ReadInt(exception, "Number") == 1062,
            _ => false
        };
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
