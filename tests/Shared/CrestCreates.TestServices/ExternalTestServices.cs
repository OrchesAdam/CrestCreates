using Npgsql;

namespace CrestCreates.TestServices;

/// <summary>
/// 长驻测试服务入口：集成测试不再每次启动容器，一律经环境变量连接预置服务
/// （见 infra/docker-compose.test-services.yml 与 infra/README.md）。
/// </summary>
public static class ExternalTestServices
{
    public static string RequiredConnectionString(string variable, string service)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"{variable} is required: point it at a long-lived {service} test instance. " +
                "Per-run Testcontainers startup has been removed; provision services via " +
                "infra/docker-compose.test-services.yml (see infra/README.md).");
        return value;
    }

    public static string RequiredContainerName(string variable, string service)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"{variable} is required: name the dedicated long-lived {service} container " +
                "that fault-injection tests may stop/start (see infra/README.md).");
        return value;
    }

    /// <summary>在长驻 PG 服务器上创建一次性隔离数据库，返回指向该库的连接串。</summary>
    public static async Task<string> CreateIsolatedDatabaseAsync(string serverConnectionString, string prefix)
    {
        if (prefix.Length == 0 || prefix.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Database prefix must be identifier-safe.", nameof(prefix));
        var database = $"{prefix}_{Guid.NewGuid():N}";
        var admin = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{database}\";";
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = database }.ConnectionString;
    }

    public static async Task DropDatabaseAsync(string serverConnectionString, string database)
    {
        var admin = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = "postgres" };
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(admin.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE);";
                await command.ExecuteNonQueryAsync();
                return;
            }
            catch (PostgresException ex) when (attempt < 4 && ex.SqlState is "57P01" or "55006")
            {
                // PG16 DROP DATABASE FORCE 与残留池连接竞争时可能以 57P01 中断自身会话，库未删成；重试。
                await Task.Delay(500);
            }
        }
    }

    public static string DatabaseName(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString).Database;
}