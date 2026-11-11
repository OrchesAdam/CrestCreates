using System;
using System.Linq;

namespace CrestCreates.Data.EFCore.Repositories;

/// <summary>
/// Recognizes provider unique-constraint exceptions without taking a compile-time dependency on
/// every database driver, and only when the provider-reported relation or constraint provably
/// identifies the model target that was inserted. Provider type and property names are the only
/// stable cross-provider signal available at this layer; anything that cannot be proven is left
/// as the original exception.
/// </summary>
internal static class EfCoreUniqueConstraintViolationDetector
{
    public static bool IsUniqueConstraintViolation(Exception exception, EfCoreUniqueConstraintTarget? target)
    {
        if (target is null)
        {
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (IsKnownUniqueViolation(current, target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKnownUniqueViolation(Exception exception, EfCoreUniqueConstraintTarget target)
    {
        return exception.GetType().FullName switch
        {
            "Npgsql.PostgresException" => IsNpgsqlUniqueViolation(exception, target),
            "Microsoft.Data.Sqlite.SqliteException" => IsSqliteUniqueViolation(exception, target),
            "Microsoft.Data.SqlClient.SqlException" => IsSqlServerUniqueViolation(exception, target),
            "MySql.Data.MySqlClient.MySqlException" or "MySqlConnector.MySqlException" =>
                IsMySqlUniqueViolation(exception, target),
            _ => false
        };
    }

    private static bool IsNpgsqlUniqueViolation(Exception exception, EfCoreUniqueConstraintTarget target)
    {
        if (ReadString(exception, "SqlState") != "23505")
        {
            return false;
        }

        var tableName = ReadString(exception, "TableName");
        var schemaName = ReadString(exception, "SchemaName");
        var expectedSchema = target.ResolveSchema();
        if (string.IsNullOrEmpty(tableName) ||
            string.IsNullOrEmpty(schemaName) ||
            string.IsNullOrEmpty(expectedSchema) ||
            !string.Equals(tableName, target.Table, StringComparison.Ordinal) ||
            !string.Equals(schemaName, expectedSchema, StringComparison.Ordinal))
        {
            return false;
        }

        var constraintName = ReadString(exception, "ConstraintName");
        return string.IsNullOrEmpty(constraintName) ||
               target.IndexNames.Contains(constraintName, StringComparer.Ordinal);
    }

    private static bool IsSqliteUniqueViolation(Exception exception, EfCoreUniqueConstraintTarget target)
    {
        if (ReadInt(exception, "SqliteErrorCode") != 19 ||
            ReadInt(exception, "SqliteExtendedErrorCode") is not (1555 or 2067))
        {
            return false;
        }

        const string marker = "UNIQUE constraint failed: ";
        var message = exception.Message;
        var markerIndex = message.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var detailStart = markerIndex + marker.Length;
        var detailEnd = message.IndexOf('\'', detailStart);
        var detail = detailEnd > detailStart ? message[detailStart..detailEnd] : message[detailStart..];

        var columns = detail
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(entry =>
            {
                var separator = entry.IndexOf('.');
                return separator <= 0 || separator == entry.Length - 1
                    ? ((string Table, string Column)?)null
                    : (Table: entry[..separator], Column: entry[(separator + 1)..]);
            })
            .ToList();

        if (columns.Count == 0 || columns.Any(column => column is null))
        {
            return false;
        }

        if (columns.Any(column => !string.Equals(column!.Value.Table, target.Table, StringComparison.Ordinal)))
        {
            return false;
        }

        var observedColumns = columns.Select(column => column!.Value.Column).ToArray();
        return target.KeyColumnSets.Any(columnSet =>
            columnSet.SequenceEqual(observedColumns, StringComparer.Ordinal));
    }

    private static bool IsSqlServerUniqueViolation(Exception exception, EfCoreUniqueConstraintTarget target)
    {
        if (ReadInt(exception, "Number") is not (2601 or 2627))
        {
            return false;
        }

        var message = exception.Message;
        var reportedObject = ExtractQuotedValue(message, "in object '");
        var objectNames = target.Schema is null
            ? new[] { target.Table, "dbo." + target.Table }
            : new[] { target.Schema + "." + target.Table };
        if (reportedObject is null ||
            !objectNames.Contains(reportedObject, StringComparer.Ordinal))
        {
            return false;
        }

        var indexName = ExtractQuotedValue(message, "with unique index '") ??
                        ExtractQuotedValue(message, "constraint '");
        return indexName is null ||
               target.IndexNames.Contains(indexName, StringComparer.Ordinal);
    }

    private static bool IsMySqlUniqueViolation(Exception exception, EfCoreUniqueConstraintTarget target)
    {
        if (ReadInt(exception, "Number") != 1062)
        {
            return false;
        }

        var key = ExtractQuotedValue(exception.Message, "for key '");
        if (key is null)
        {
            return false;
        }

        var separator = key.LastIndexOf('.');
        var tablePart = separator > 0 ? key[..separator] : null;
        var indexName = separator > 0 ? key[(separator + 1)..] : key;
        if (tablePart is not null &&
            !string.Equals(tablePart, target.Table, StringComparison.Ordinal))
        {
            return false;
        }

        return target.IndexNames.Contains(indexName, StringComparer.Ordinal);
    }

    private static string? ExtractQuotedValue(string message, string marker)
    {
        var markerIndex = message.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var valueStart = markerIndex + marker.Length;
        var valueEnd = message.IndexOf('\'', valueStart);
        return valueEnd > valueStart ? message[valueStart..valueEnd] : null;
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
