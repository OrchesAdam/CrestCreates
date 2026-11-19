using CrestCreates.TestServices;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;

public sealed class PostgreSqlRuntimeCollectionFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public Task InitializeAsync()
    {
        ConnectionString = ExternalTestServices.RequiredConnectionString(
            "CREST_RUNTIME_PG_CONNECTION", "PostgreSQL 16");
        return Task.CompletedTask;
    }

    public async Task<PostgreSqlRuntimeSchemaLease> CreateSchemaLeaseAsync()
    {
        var schema = $"itest_{Guid.NewGuid():N}";
        var options = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = ConnectionString,
            Schema = schema
        };
        var runner = new PostgreSqlRuntimeMigrationRunner(options);
        await runner.ApplyAsync(new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });
        return new PostgreSqlRuntimeSchemaLease(ConnectionString, options);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class PostgreSqlRuntimeSchemaLease : IAsyncDisposable
{
    private readonly string _connectionString;
    private bool _disposed;

    internal PostgreSqlRuntimeSchemaLease(string connectionString, PostgreSqlRuntimePersistenceOptions options)
    {
        _connectionString = connectionString;
        Options = options;
    }

    public PostgreSqlRuntimePersistenceOptions Options { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"drop schema if exists \"{Options.Schema}\" cascade;", connection);
        await command.ExecuteNonQueryAsync();
    }
}