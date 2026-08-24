using System.Diagnostics;
using System.Runtime.InteropServices;
using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using FluentAssertions;
using Testcontainers.PostgreSql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests;

public sealed class PostgreSqlRuntimeAotFixtureTests
{
    [Fact]
    public async Task DurableControlPlaneReferenceDataAotFixture_Should_PublishLinkAndRun()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw Xunit.Sdk.SkipException.ForSkip("The PostgreSQL Runtime NativeAOT gate is pinned to linux-x64.");

        foreach (var scenario in Enum.GetNames<AotScenarioVariant>())
            ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.C12, "Composition", scenario, EvidenceVectorKey.Default, RequiredRunner.Aot);

        var root = FindRepositoryRoot();
        var output = Path.Combine(Path.GetTempPath(), "crest-runtime-postgresql-aot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var connectionString = Environment.GetEnvironmentVariable("CREST_RUNTIME_PG_CONNECTION");
        PostgreSqlContainer? postgres = null;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("crest_runtime_aot")
                .WithUsername("crest")
                .WithPassword("crest")
                .Build();
        }

        try
        {
            if (postgres is not null)
            {
                await postgres.StartAsync();
                connectionString = postgres.GetConnectionString();
            }
            var project = Path.Combine(root,
                "tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/CrestCreates.Runtime.Persistence.PostgreSql.AotHost.csproj");
            var publish = await RunAsync(
                "dotnet",
                $"publish \"{project}\" -c Release -r linux-x64 --self-contained true -p:CrestCreatesPublishMode=aot --disable-build-servers -o \"{output}\"",
                TimeSpan.FromMinutes(8));
            publish.ExitCode.Should().Be(0, publish.Output);
            publish.Output.Should().NotContain("warning IL2026");
            publish.Output.Should().NotContain("warning IL3050");

            var executable = Path.Combine(output, "CrestCreates.Runtime.Persistence.PostgreSql.AotHost");
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            var schema = "itest_" + Guid.NewGuid().ToString("N");
            var execution = await RunAsync(
                executable,
                $"\"{connectionString}\" {schema}",
                TimeSpan.FromMinutes(5));
            execution.ExitCode.Should().Be(0, execution.Output);
            execution.Output.Should().Contain("PHASE9B_POSTGRES_SUSPENSION_OK");
            execution.Output.Should().Contain("PHASE9B_POSTGRES_STATE_OK");
            execution.Output.Should().Contain("PHASE9B_POSTGRES_PIN_RECOVERY_OK");
            execution.Output.Should().Contain("PHASE9B_POSTGRES_AUDIT_RETRY_OK");
            execution.Output.Should().Contain("PHASE9C_POSTGRES_OUTBOX_AOT_OK");
            execution.Output.Should().Contain("CRESTCREATES_DURABLE_CONTROL_PLANE_WORKFLOW_CAPABILITY_OK");
            execution.Output.Should().Contain("CRESTCREATES_DURABLE_CONTROL_PLANE_WORKFLOW_HUMAN_TASK_OK");
            execution.Output.Should().Contain("CRESTCREATES_DURABLE_CONTROL_PLANE_WORKFLOW_SUBWORKFLOW_OK");
            execution.Output.Should().Contain("CRESTCREATES_DURABLE_REFERENCE_ORGANIZATION_OK");
            execution.Output.Should().Contain("CRESTCREATES_DURABLE_REFERENCE_DATA_PERMISSION_OK");
            execution.Output.Should().Contain("CRESTCREATES_DURABLE_CONTROL_PLANE_REFERENCE_DATA_OK");
            // Real CrashWorker-style subprocess commit → kill → fresh-process recovery
            // for each of the five pre-dispatch crash windows.
            execution.Output.Should().Contain("CRESTCREATES_AGENTTOOL_PREDISPATCH_CW04_OK");
            execution.Output.Should().Contain("CRESTCREATES_AGENTTOOL_PREDISPATCH_CW05_OK");
            execution.Output.Should().Contain("CRESTCREATES_AGENTTOOL_PREDISPATCH_CW07_OK");
            execution.Output.Should().Contain("CRESTCREATES_AGENTTOOL_PREDISPATCH_CW08_OK");
            execution.Output.Should().Contain("CRESTCREATES_AGENTTOOL_PREDISPATCH_CW09_OK");
            execution.Output.Should().Contain("CRESTCREATES_DURABLE_AGENT_TOOL_PREDISPATCH_OK");
            execution.Output.Should().Contain("CRESTCREATES_DURABLE_AGENT_MEMORY_OK");
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); } catch { }
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string fileName, string arguments, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Process '{fileName}' exceeded {timeout}.");
        }

        return (process.ExitCode, await stdout + await stderr);
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
