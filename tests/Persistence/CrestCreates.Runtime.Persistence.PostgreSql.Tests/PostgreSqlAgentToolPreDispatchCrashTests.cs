using System.Diagnostics;
using CrestCreates.Agent.Tools;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

/// <summary>
/// Real subprocess crash windows for the Phase 9b+ pre-dispatch state machine.
/// Each test starts the CrashWorker with a scenario that performs the durable
/// writes for a crash window, reads the commit sentinel, kills the process tree,
/// waits for the PostgreSQL backend to exit, and only then creates a fresh
/// provider and runs the reconciler — proving cross-process convergence without
/// any dispatcher call.
/// </summary>
[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentToolPreDispatchCrashTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    [Theory]
    [InlineData("predispatch-cw04-budget-committed", "PREDISPATCH_CW04_BUDGET_COMMITTED",
        AgentToolInvocationPreDispatchState.Abandoned)]
    [InlineData("predispatch-cw05-reservation-returned", "PREDISPATCH_CW05_RESERVATION_RETURNED",
        AgentToolInvocationPreDispatchState.Abandoned)]
    [InlineData("predispatch-cw07-record-ambiguous", "PREDISPATCH_CW07_RECORD_AMBIGUOUS",
        AgentToolInvocationPreDispatchState.Abandoned)]
    public async Task PreDispatchCrash_BeforeCheckpoint_Should_ReleaseBudgetAndAbandonGate(
        string scenario, string sentinel, AgentToolInvocationPreDispatchState expectedGateState)
    {
        var (attemptId, lease) = await RunWorkerAndKillAsync(scenario, sentinel);
        await using var _ = lease;
        var identity = SampleIdentity(attemptId);

        using var fresh = BuildFreshProvider(lease.Options);
        var gate = fresh.GetRequiredService<IAgentToolInvocationGate>();
        var budget = fresh.GetRequiredService<IAgentToolBudgetGate>();
        var auditor = fresh.GetRequiredService<IAgentToolGovernanceAuditor>();
        var store = fresh.GetRequiredService<IAgentToolPreDispatchReconciliationStore>();
        var reconciler = new DefaultAgentToolPreDispatchReconciler(gate, budget, auditor, store);

        // Fresh provider converges the crash window with zero dispatcher calls.
        // The worker process tree was killed, so the reconciler asserts durable
        // ownership loss to claim the still-valid lease before settling anything.
        var result = await reconciler.ReconcileAsync(
            identity,
            cancellationToken: default,
            context: new AgentToolPreDispatchReconciliationContext
            {
                OwnershipLost = true,
                OwnershipEvidence = "process-tree-killed"
            });
        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);
        result.Receipt.Should().NotBeNull();
        result.Receipt!.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);

        var budgetState = await budget.GetReservationStateAsync(identity);
        budgetState.Status.Should().Be(AgentToolBudgetReadStatus.Released);

        var gateState = await gate.GetPreDispatchStateAsync(identity);
        gateState.State.Should().Be(expectedGateState);

        // A second reconciliation is a no-op projection of the same receipt.
        var again = await reconciler.ReconcileAsync(identity);
        again.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.AlreadyReleased);
    }

    [Theory]
    [InlineData("predispatch-cw08-checkpoint-committed", "PREDISPATCH_CW08_CHECKPOINT_COMMITTED")]
    [InlineData("predispatch-cw09-receipt-obtained", "PREDISPATCH_CW09_RECEIPT_OBTAINED")]
    public async Task PreDispatchCrash_AfterCheckpoint_Should_ReleaseGateWithoutDispatch(
        string scenario, string sentinel)
    {
        var (attemptId, lease) = await RunWorkerAndKillAsync(scenario, sentinel);
        await using var _ = lease;
        var identity = SampleIdentity(attemptId);

        using var fresh = BuildFreshProvider(lease.Options);
        var gate = fresh.GetRequiredService<IAgentToolInvocationGate>();
        var budget = fresh.GetRequiredService<IAgentToolBudgetGate>();
        var auditor = fresh.GetRequiredService<IAgentToolGovernanceAuditor>();
        var store = fresh.GetRequiredService<IAgentToolPreDispatchReconciliationStore>();
        var reconciler = new DefaultAgentToolPreDispatchReconciler(gate, budget, auditor, store);

        var result = await reconciler.ReconcileAsync(
            identity,
            cancellationToken: default,
            context: new AgentToolPreDispatchReconciliationContext
            {
                OwnershipLost = true,
                OwnershipEvidence = "process-tree-killed"
            });
        result.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.Released);
        result.Receipt.Should().NotBeNull();

        var budgetState = await budget.GetReservationStateAsync(identity);
        budgetState.Status.Should().Be(AgentToolBudgetReadStatus.Released);

        var gateState = await gate.GetPreDispatchStateAsync(identity);
        gateState.State.Should().Be(AgentToolInvocationPreDispatchState.Released);

        var again = await reconciler.ReconcileAsync(identity);
        again.Status.Should().Be(AgentToolPreDispatchReconciliationStatus.AlreadyReleased);
    }

    /// <summary>
    /// Starts the CrashWorker for the given scenario, waits for the exact
    /// sentinel, kills the process tree, waits for the backend to exit, and
    /// returns the AttemptId that the fresh-provider recovery needs plus the
    /// schema lease (created before the worker runs) so the caller keeps it
    /// alive through reconciliation.
    /// </summary>
    private async Task<(string AttemptId, PostgreSqlRuntimeSchemaLease Lease)> RunWorkerAndKillAsync(
        string scenario,
        string sentinel)
    {
        var lease = await fixture.CreateSchemaLeaseAsync();
        var worker = PostgreSqlCrashWorkerPath.Resolve();
        File.Exists(worker).Should().BeTrue("the CrashWorker is a CI-built test artifact");

        var applicationName = "predispatch-crash-" + Guid.NewGuid().ToString("N");
        var connectionBuilder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            ApplicationName = applicationName
        };
        using var workerProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{worker}\" \"{connectionBuilder.ConnectionString}\" {lease.Options.Schema} {Guid.NewGuid():N} {applicationName} {scenario}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        workerProcess.Start();
        using var readyTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var stderrTask = workerProcess.StandardError.ReadToEndAsync(readyTimeout.Token);
        var marker = await workerProcess.StandardOutput.ReadLineAsync(readyTimeout.Token);
        if (marker is null)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException($"CrashWorker produced no marker. Stderr: {stderr}");
        }

        var parts = marker.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        parts.Length.Should().Be(2, $"sentinel should be '{sentinel} <attemptId>', got '{marker}'");
        parts[0].Should().Be(sentinel);

        workerProcess.Kill(entireProcessTree: true);
        await workerProcess.WaitForExitAsync();
        await WaitForBackendExitAsync(applicationName);
        return (parts[1], lease);
    }

    private static ServiceProvider BuildFreshProvider(PostgreSqlRuntimePersistenceOptions options)
        => new ServiceCollection().AddCrestCreatesPostgreSqlRuntimePersistence(options).BuildServiceProvider();

    private static AgentToolPreDispatchIdentity SampleIdentity(string attemptId)
    {
        // The CrashWorker uses the same deterministic logical invocation key.
        var key = new AgentToolLogicalInvocationKey("crash", "user", "agent", "exec", "predispatch");
        return new AgentToolPreDispatchIdentity(key, attemptId);
    }

    private async Task WaitForBackendExitAsync(string applicationName)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "select count(*) from pg_stat_activity where application_name=@application;", connection);
            command.Parameters.AddWithValue("application", applicationName);
            if ((long)(await command.ExecuteScalarAsync())! == 0)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException("The crash worker PostgreSQL backend did not exit.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
