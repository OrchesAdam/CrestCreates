using CrestCreates.Metadata.Abstractions.Bootstrap;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Performs startup compatibility validation before a Host accepts Runtime
/// work. Validation-only mode performs no DDL; DDL requires explicit opt-in.
/// </summary>
internal sealed class PostgreSqlRuntimeSchemaCompatibilityHostedService : IHostedService, IBootstrapTask
{
    private readonly PostgreSqlRuntimeMigrationRunner _migrations;
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task? _readyTask;

    public PostgreSqlRuntimeSchemaCompatibilityHostedService(
        PostgreSqlRuntimeMigrationRunner migrations,
        PostgreSqlRuntimePersistenceOptions options)
    {
        _migrations = migrations;
        _options = options;
    }

    public string TaskId => "runtime-schema-compatibility";
    public Type ServiceType => typeof(PostgreSqlRuntimeSchemaCompatibilityHostedService);
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();
    public bool IsRequired => true;

    public Task StartAsync(CancellationToken cancellationToken) => EnsureSchemaReadyAsync(cancellationToken);

    public Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken ct) => EnsureSchemaReadyAsync(ct);

    private async Task EnsureSchemaReadyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _readyTask ??= _migrations.ApplyAsync(
                new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = _options.ApplyMigrations },
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        await _readyTask.ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
