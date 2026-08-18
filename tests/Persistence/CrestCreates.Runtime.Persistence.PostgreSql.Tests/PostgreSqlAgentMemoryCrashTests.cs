using System.Diagnostics;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Runtime.Persistence.PostgreSql;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

/// <summary>
/// Real process-crash evidence: kill the CrashWorker process tree at a
/// documented durable window and read the outcome through a fresh provider.
/// </summary>
[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentMemoryCrashTests : IAsyncLifetime
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;
    private PostgreSqlRuntimeSchemaLease _lease = null!;

    public PostgreSqlAgentMemoryCrashTests(PostgreSqlRuntimeCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
        => _lease = await _fixture.CreateSchemaLeaseAsync();

    public async Task DisposeAsync()
        => await _lease.DisposeAsync();

    [Fact]
    public async Task CrashBeforeCurationCommit_Should_ExposeNoMutationAfterBackendExit()
    {
        var applicationName = $"agent-memory-crash-before-{Guid.NewGuid():N}";
        var operationId = $"op-crash-before-{Guid.NewGuid():N}";

        await RunWorkerAsync("agent-memory-before-promote-commit", applicationName, operationId);
        await WaitForBackendExitAsync(applicationName);

        await using var driver = new PostgreSqlAgentMemoryContractDriver(_lease);
        (await driver.MemoryStore.GetMemoryAsync("crash-tenant", "memory-crash")).Should().BeNull();
        var candidate = await driver.MemoryStore.GetCandidateAsync("crash-tenant", "candidate-crash");
        candidate!.Status.Should().Be(AgentMemoryStatus.Candidate, "crash before COMMIT must leave the Candidate unchanged.");
    }

    [Fact]
    public async Task CrashAfterCurationCommit_Should_RemainVisibleToFreshProcess()
    {
        var applicationName = $"agent-memory-crash-after-{Guid.NewGuid():N}";
        var operationId = $"op-crash-after-{Guid.NewGuid():N}";

        await RunWorkerAsync("agent-memory-after-promote-commit", applicationName, operationId);
        await WaitForBackendExitAsync(applicationName);

        await using var driver = new PostgreSqlAgentMemoryContractDriver(_lease);
        var memory = await driver.MemoryStore.GetMemoryAsync("crash-tenant", "memory-crash");
        memory.Should().NotBeNull("crash after COMMIT must remain durable.");
        memory!.Status.Should().Be(AgentMemoryStatus.Active);
        var candidate = await driver.MemoryStore.GetCandidateAsync("crash-tenant", "candidate-crash");
        candidate!.Status.Should().Be(AgentMemoryStatus.Active);
    }

    private async Task RunWorkerAsync(string scenario, string applicationName, string operationId)
    {
        var worker = PostgreSqlCrashWorkerPath.Resolve();
        File.Exists(worker).Should().BeTrue("the CrashWorker is a CI-built test artifact");

        var connectionBuilder = new NpgsqlConnectionStringBuilder(_lease.Options.ConnectionString)
        {
            ApplicationName = applicationName
        };

        using var workerProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{worker}\" \"{connectionBuilder.ConnectionString}\" {_lease.Options.Schema} {operationId} {applicationName} {scenario}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        workerProcess.Start();
        using var readyTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var stderrTask = workerProcess.StandardError.ReadToEndAsync(readyTimeout.Token);
        var marker = await workerProcess.StandardOutput.ReadLineAsync(readyTimeout.Token);
        if (marker is null)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException($"CrashWorker produced no marker. Stderr: {stderr}");
        }

        marker.Should().Contain("AGENT_MEMORY_", "the worker must reach its durable window");
        workerProcess.Kill(entireProcessTree: true);
        await workerProcess.WaitForExitAsync();
    }

    private async Task WaitForBackendExitAsync(string applicationName)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var connection = new NpgsqlConnection(_lease.Options.ConnectionString);
        await connection.OpenAsync(timeout.Token);
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await using var command = new NpgsqlCommand(
                "select count(*) from pg_stat_activity where application_name=@name;",
                connection);
            command.Parameters.AddWithValue("name", applicationName);
            var active = (long)(await command.ExecuteScalarAsync(timeout.Token))!;
            if (active == 0)
                return;
            await Task.Delay(200, timeout.Token);
        }
    }

}
