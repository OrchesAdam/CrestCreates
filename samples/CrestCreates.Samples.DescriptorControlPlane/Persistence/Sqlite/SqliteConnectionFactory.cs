using Microsoft.Data.Sqlite;

namespace CrestCreates.Samples.DescriptorControlPlane;

/// <summary>
/// Creates SQLite database connections for the Company Certification sample.
/// Each call to <see cref="CreateConnection"/> returns a new open connection
/// that the caller must dispose. The factory itself holds no disposable resources.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(CompanyCertificationPersistenceOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabasePath))
            throw new InvalidOperationException(
                "DatabasePath must be set when using SQLite persistence mode.");

        var directory = Path.GetDirectoryName(options.DatabasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ConnectionString;
    }

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "PRAGMA busy_timeout = 5000;";
        cmd.ExecuteNonQuery();

        // WAL is a persistent database-level setting; set on first connection
        // and harmless to repeat on subsequent connections.
        cmd.CommandText = "PRAGMA journal_mode = WAL;";
        cmd.ExecuteNonQuery();

        return connection;
    }
}
