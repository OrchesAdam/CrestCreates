using Microsoft.Data.Sqlite;

namespace CrestCreates.Samples.DescriptorControlPlane;

/// <summary>
/// Initializes the SQLite database schema. Idempotent — safe to call on every startup.
/// </summary>
public sealed class SqliteDatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteDatabaseInitializer(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public void Initialize()
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            CreateCompanyCertificationsTable(connection);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void CreateCompanyCertificationsTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS company_certifications (
                id TEXT NOT NULL PRIMARY KEY,
                company_name TEXT NOT NULL,
                unified_social_credit_code TEXT NOT NULL,
                certification_type TEXT NOT NULL,
                application_date TEXT,
                notes TEXT,
                status INTEGER NOT NULL,
                reviewer_notes TEXT,
                reviewer_decision TEXT,
                reviewed_by TEXT
            )
            """;
        cmd.ExecuteNonQuery();
    }

}
