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
            CreateWorkflowInstancesTable(connection);
            CreateHumanTaskInstancesTable(connection);
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

    private static void CreateWorkflowInstancesTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS workflow_instances (
                instance_id TEXT NOT NULL PRIMARY KEY,
                workflow_descriptor_id TEXT NOT NULL,
                workflow_descriptor_version INTEGER NOT NULL,
                workflow_selection_mode INTEGER NOT NULL,
                workflow_expected_contract_hash TEXT,
                status INTEGER NOT NULL,
                current_step_id TEXT,
                step_index INTEGER NOT NULL,
                waiting_human_task_id TEXT,
                started_at TEXT NOT NULL,
                updated_at TEXT,
                completed_at TEXT,
                variables TEXT,
                step_variables TEXT,
                step_results TEXT,
                error_message TEXT,
                concurrency_stamp TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
    }

    private static void CreateHumanTaskInstancesTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS human_task_instances (
                id TEXT NOT NULL PRIMARY KEY,
                human_task_id TEXT NOT NULL,
                human_task_version INTEGER NOT NULL,
                status INTEGER NOT NULL,
                tenant_id TEXT,
                assignee_user_id TEXT,
                assignee_role_id TEXT,
                workflow_instance_id TEXT,
                workflow_step_id TEXT,
                input TEXT,
                output TEXT,
                outcome TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT,
                completed_at TEXT,
                cancelled_at TEXT,
                cancellation_reason TEXT,
                candidate_user_ids TEXT,
                candidate_role_ids TEXT,
                organization_unit_id TEXT,
                position_id TEXT,
                assignee_resolution_reason TEXT,
                concurrency_stamp TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
    }
}
