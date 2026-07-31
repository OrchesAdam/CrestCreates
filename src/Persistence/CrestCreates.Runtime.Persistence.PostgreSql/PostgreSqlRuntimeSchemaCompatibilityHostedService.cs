using Microsoft.Extensions.Hosting;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Performs startup compatibility validation before a Host accepts Runtime
/// work. Validation-only mode performs no DDL; DDL requires explicit opt-in.
/// </summary>
internal sealed class PostgreSqlRuntimeSchemaCompatibilityHostedService : IHostedService
{
    private readonly PostgreSqlRuntimeMigrationRunner _migrations;
    private readonly PostgreSqlRuntimePersistenceOptions _options;

    public PostgreSqlRuntimeSchemaCompatibilityHostedService(
        PostgreSqlRuntimeMigrationRunner migrations,
        PostgreSqlRuntimePersistenceOptions options)
    {
        _migrations = migrations;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => _migrations.ApplyAsync(
            new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = _options.ApplyMigrations },
            cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
