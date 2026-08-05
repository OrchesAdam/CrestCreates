using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;

public sealed class PostgreSqlRuntimeCollectionFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _container;

    public PostgreSqlRuntimeCollectionFixture()
    {
        // Allow running the PostgreSQL contract tests against a local server without Docker.
        var external = Environment.GetEnvironmentVariable("CREST_RUNTIME_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(external))
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("crest_runtime_tests")
                .WithUsername("crest")
                .WithPassword("crest")
                .Build();
        }
    }

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        else
        {
            ConnectionString = Environment.GetEnvironmentVariable("CREST_RUNTIME_PG_CONNECTION")!;
        }
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

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
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
