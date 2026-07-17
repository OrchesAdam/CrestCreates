using Microsoft.Data.Sqlite;

namespace CrestCreates.Samples.DescriptorControlPlane;

/// <summary>
/// Sample-only diagnostics for verifying SQLite store state in tests.
/// Not part of the IWorkflowInstanceStore/IHumanTaskInstanceStore interfaces.
/// </summary>
public sealed class SqliteRuntimeStoreDiagnostics
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteRuntimeStoreDiagnostics(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> CountWorkflowInstancesAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM workflow_instances";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? (int)l : 0;
    }

    public async Task<int> CountHumanTaskInstancesAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM human_task_instances";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? (int)l : 0;
    }

    public async Task<int> CountCompanyCertificationsAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM company_certifications";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? (int)l : 0;
    }
}
