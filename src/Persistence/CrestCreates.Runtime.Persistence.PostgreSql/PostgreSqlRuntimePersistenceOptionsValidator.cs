using System.Text.RegularExpressions;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal static class PostgreSqlRuntimePersistenceOptionsValidator
{
    private static readonly Regex SchemaName = new(
        "^[a-z_][a-z0-9_]{0,62}$",
        RegexOptions.CultureInvariant);

    public static void Validate(PostgreSqlRuntimePersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("PostgreSQL runtime connection string is required.", nameof(options));
        if (!SchemaName.IsMatch(options.Schema))
            throw new ArgumentException("PostgreSQL runtime schema must be a lower-case PostgreSQL identifier.", nameof(options));
        if (options.CommandTimeoutSeconds is <= 0 or > 600)
            throw new ArgumentOutOfRangeException(nameof(options), "PostgreSQL Runtime command timeout must be between 1 and 600 seconds.");
    }
}
