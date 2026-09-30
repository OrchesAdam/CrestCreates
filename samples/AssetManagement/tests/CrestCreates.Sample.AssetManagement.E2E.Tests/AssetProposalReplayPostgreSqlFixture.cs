using CrestCreates.Runtime.Persistence.PostgreSql;
using Npgsql;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

internal sealed class AssetProposalReplayPostgreSqlFixture : IAsyncLifetime
{
    private readonly string _schema = $"itest_{Guid.NewGuid():N}";

    public AssetProposalReplayPostgreSqlFixture()
    {
        ConnectionString = Environment.GetEnvironmentVariable("ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING must point at the durable PostgreSQL test service.");
    }

    public string ConnectionString { get; private set; } = string.Empty;

    public PostgreSqlRuntimePersistenceOptions Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Options = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = ConnectionString,
            Schema = _schema
        };
        await new PostgreSqlRuntimeMigrationRunner(Options).ApplyAsync(
            new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true });
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"drop schema if exists \"{Options.Schema}\" cascade;", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
