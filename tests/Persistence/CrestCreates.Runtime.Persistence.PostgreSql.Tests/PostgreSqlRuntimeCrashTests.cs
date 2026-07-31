using System.Diagnostics;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlRuntimeCrashTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    [Fact]
    public async Task CommitResponseLoss_ShouldPreserveCommittedStateForFreshProviderReconciliation()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var root = FindRepositoryRoot();
        var worker = Path.Combine(root,
            "tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/bin/Debug/net10.0/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker.dll");
        File.Exists(worker).Should().BeTrue("the CrashWorker is a CI-built test artifact");

        var operationId = "crash-" + Guid.NewGuid().ToString("N");
        var applicationName = "phase9b-crash-" + Guid.NewGuid().ToString("N");
        var connectionBuilder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            ApplicationName = applicationName
        };
        using var workerProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{worker}\" \"{connectionBuilder.ConnectionString}\" {lease.Options.Schema} {operationId} {applicationName} commit-without-response",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        workerProcess.Start();
        using var readyTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var marker = await workerProcess.StandardOutput.ReadLineAsync(readyTimeout.Token);
        marker.Should().Be($"COMMITTED {operationId}");

        workerProcess.Kill(entireProcessTree: true);
        await workerProcess.WaitForExitAsync();
        await WaitForBackendExitAsync(applicationName);

        using var freshProvider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .BuildServiceProvider();
        var workflowKey = new RuntimeInstanceKey("crash", "workflow");
        var taskKey = new RuntimeInstanceKey("crash", "task");
        var workflow = await freshProvider.GetRequiredService<IWorkflowInstanceStore>().GetAsync(workflowKey);
        var task = await freshProvider.GetRequiredService<IHumanTaskInstanceStore>().GetAsync(taskKey);
        var receipt = await freshProvider.GetRequiredService<IWorkflowSuspensionReceiptStore>()
            .GetAsync(new RuntimeTenantScope("crash"), operationId);
        workflow.Should().NotBeNull();
        workflow!.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        workflow.WaitingHumanTaskKey.Should().Be(taskKey);
        task.Should().NotBeNull();
        receipt.Should().NotBeNull();
        receipt!.WorkflowToRevision.Should().Be(receipt.WorkflowFromRevision + 1);
    }

    [Fact]
    public async Task CrashBetweenHumanTaskAndWorkflowWrite_ShouldExposeNoPartialSuspension()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var root = FindRepositoryRoot();
        var worker = Path.Combine(root,
            "tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/bin/Debug/net10.0/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker.dll");
        File.Exists(worker).Should().BeTrue("the CrashWorker is a CI-built test artifact");

        var operationId = "crash-f01-" + Guid.NewGuid().ToString("N");
        var applicationName = "phase9b-crash-f01-" + Guid.NewGuid().ToString("N");
        var connectionBuilder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            ApplicationName = applicationName
        };
        using var workerProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{worker}\" \"{connectionBuilder.ConnectionString}\" {lease.Options.Schema} {operationId} {applicationName} crash-after-human-task-insert",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        workerProcess.Start();
        using var readyTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var marker = await workerProcess.StandardOutput.ReadLineAsync(readyTimeout.Token);
        marker.Should().Be("HUMAN_TASK_INSERTED");

        workerProcess.Kill(entireProcessTree: true);
        await workerProcess.WaitForExitAsync();
        await WaitForBackendExitAsync(applicationName);

        using var freshProvider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .BuildServiceProvider();
        var workflowKey = new RuntimeInstanceKey("crash", "workflow");
        var taskKey = new RuntimeInstanceKey("crash", "task");
        var workflow = await freshProvider.GetRequiredService<IWorkflowInstanceStore>().GetAsync(workflowKey);
        var task = await freshProvider.GetRequiredService<IHumanTaskInstanceStore>().GetAsync(taskKey);
        var receipt = await freshProvider.GetRequiredService<IWorkflowSuspensionReceiptStore>()
            .GetAsync(new RuntimeTenantScope("crash"), operationId);

        workflow.Should().NotBeNull();
        workflow!.Status.Should().Be(WorkflowInstanceStatus.Running,
            "the Workflow CAS was never executed, so the workflow must remain Running");
        workflow.WaitingHumanTaskKey.Should().BeNull();
        task.Should().BeNull("the HumanTask INSERT was rolled back with the aborted transaction");
        receipt.Should().BeNull("the Receipt was never written");
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
