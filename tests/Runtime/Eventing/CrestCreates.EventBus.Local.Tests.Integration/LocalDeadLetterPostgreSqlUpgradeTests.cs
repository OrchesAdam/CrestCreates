using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.DeadLetter.EFCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

/// <summary>
/// Real PostgreSQL evidence for the additive LocalContextVersion migration: an old-schema database
/// (the final model minus that column) is upgraded twice with the repeatable script and then read
/// and written through fresh-scope stores. EF InMemory cannot prove DDL idempotence or the real
/// column round trip. Provider status beyond EF Core + Npgsql remains separately declared.
/// </summary>
public sealed class LocalDeadLetterPostgreSqlUpgradeTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _connectionString = string.Empty;
    private string _schema = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("CREST_LOCAL_EVENT_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(external))
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("crest_local_event_tests")
                .WithUsername("crest")
                .WithPassword("crest")
                .Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }
        else
        {
            _connectionString = external;
        }

        _schema = $"itest_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA \"{_schema}\"";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE";
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private DeadLetterDbContext CreateContext()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            SearchPath = _schema,
        };
        var options = new DbContextOptionsBuilder<DeadLetterDbContext>()
            .UseNpgsql(connectionString.ConnectionString)
            .Options;
        return new DeadLetterDbContext(options);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    [Fact]
    public async Task Old_schema_upgrades_twice_and_round_trips_context_version()
    {
        // Pre-#146 shape of the shared table: everything except the additive column.
        await ExecuteAsync("""
            CREATE TABLE "DeadLetters" (
                "MessageId" text NOT NULL PRIMARY KEY,
                "EventName" text NOT NULL,
                "EventVersion" integer NOT NULL,
                "EventDescriptorId" text NULL,
                "CorrelationId" text NULL,
                "TenantId" text NULL,
                "Scope" text NOT NULL,
                "PayloadTypeFullName" text NOT NULL,
                "Payload" bytea NOT NULL,
                "ErrorMessage" text NOT NULL,
                "ExceptionType" text NULL,
                "OccurredAt" timestamp with time zone NOT NULL,
                "FailedAt" timestamp with time zone NOT NULL,
                "RetryCount" integer NOT NULL,
                "MaxRetries" integer NOT NULL,
                "Status" text NOT NULL,
                "LocalReplayKey" text NULL
            );
            CREATE INDEX "IX_DeadLetters_EventName_EventVersion" ON "DeadLetters" ("EventName", "EventVersion");
            """);
        await ExecuteAsync("""
            INSERT INTO "DeadLetters"
                ("MessageId", "EventName", "EventVersion", "EventDescriptorId", "CorrelationId", "TenantId",
                 "Scope", "PayloadTypeFullName", "Payload", "ErrorMessage", "ExceptionType",
                 "OccurredAt", "FailedAt", "RetryCount", "MaxRetries", "Status", "LocalReplayKey")
            VALUES
                ('itest-legacy', 'My.Legacy.Event', 1, NULL, NULL, NULL,
                 'Local', 'My.Legacy.Event', convert_to('legacy-payload', 'UTF8'), 'legacy failure', NULL,
                 now(), now(), 0, 3, 'Pending', 'itest:legacy');
            """);

        await using (var preUpgrade = CreateContext())
        {
            (await DeadLetterSchemaUpgrade.HasPostgreSqlLocalContextVersionColumnAsync(preUpgrade, CancellationToken.None))
                .Should().BeFalse("the old schema must genuinely lack the column before the upgrade");
        }

        // The migration is repeatable: applying it twice is idempotent.
        await using (var firstUpgrade = CreateContext())
        {
            await DeadLetterSchemaUpgrade.ApplyPostgreSqlAsync(firstUpgrade, CancellationToken.None);
        }

        await using (var secondUpgrade = CreateContext())
        {
            await DeadLetterSchemaUpgrade.ApplyPostgreSqlAsync(secondUpgrade, CancellationToken.None);
        }

        await using (var postUpgrade = CreateContext())
        {
            (await DeadLetterSchemaUpgrade.HasPostgreSqlLocalContextVersionColumnAsync(postUpgrade, CancellationToken.None))
                .Should().BeTrue();
        }

        // Fresh-scope store on the upgraded schema: the legacy row is preserved and readable.
        await using (var freshRead = CreateContext())
        {
            var store = new EfCoreDeadLetterStore(freshRead);
            var legacy = await store.GetByIdAsync("itest-legacy", CancellationToken.None);
            legacy.Should().NotBeNull();
            legacy!.LocalContextVersion.Should().BeNull("pre-migration rows stay legacy, never backfilled");
            legacy.LocalReplayKey.Should().Be("itest:legacy");
            legacy.TenantId.Should().BeNull();
            legacy.Status.Should().Be(DeadLetterStatus.Pending);
        }

        // New-format writes and state transitions work against the upgraded schema.
        await using (var freshWrite = CreateContext())
        {
            var store = new EfCoreDeadLetterStore(freshWrite);
            var modern = new DeadLetterMessage(
                "itest-modern",
                "My.Event",
                1,
                null,
                "corr-1",
                "tenant-a",
                EventScope.Local,
                typeof(LocalDeadLetterPostgreSqlUpgradeTests).AssemblyQualifiedName!,
                "{}"u8.ToArray(),
                "failure",
                null,
                DateTime.UtcNow,
                DateTime.UtcNow,
                0,
                3,
                DeadLetterStatus.Pending,
                LocalReplayKey: "itest:modern",
                LocalContextVersion: LocalEventExecutionContextSnapshot.CurrentContextVersion);
            await store.EnqueueAsync(modern, CancellationToken.None);
        }

        await using (var verify = CreateContext())
        {
            var store = new EfCoreDeadLetterStore(verify);
            var roundTripped = await store.GetByIdAsync("itest-modern", CancellationToken.None);
            roundTripped.Should().NotBeNull();
            roundTripped!.LocalContextVersion.Should().Be(1);
            roundTripped.TenantId.Should().Be("tenant-a");
            roundTripped.CorrelationId.Should().Be("corr-1");
            roundTripped.LocalReplayKey.Should().Be("itest:modern");

            await store.MarkRetriedAsync("itest-modern", CancellationToken.None);
            var transitioned = await store.GetByIdAsync("itest-modern", CancellationToken.None);
            transitioned!.Status.Should().Be(DeadLetterStatus.Retried);
            (await store.CountAsync(null, CancellationToken.None)).Should().Be(2);
        }
    }
}
