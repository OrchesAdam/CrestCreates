using System.Runtime.InteropServices;
using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.TestServices;
using FluentAssertions;
using Npgsql;
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
        var connectionString = ExternalTestServices.RequiredConnectionString(
            "CREST_RUNTIME_PG_CONNECTION", "PostgreSQL 16");
        var output = Path.Combine(Path.GetTempPath(), "crest-runtime-postgresql-aot-" + Guid.NewGuid().ToString("N"));
        var schemas = new List<string>();
        Exception? failure = null;

        try
        {
            Directory.CreateDirectory(output);
            var project = Path.Combine(root,
                "tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/CrestCreates.Runtime.Persistence.PostgreSql.AotHost.csproj");
            var publish = await TestServiceControl.RunProcessAsync("dotnet",
                ["publish", project, "-c", "Release", "-r", "linux-x64", "--self-contained", "true",
                    "-p:CrestCreatesPublishMode=aot", "--disable-build-servers", "-o", output],
                TimeSpan.FromMinutes(8), new Dictionary<string, string?> { ["MSBUILDDISABLENODEREUSE"] = "1" });
            publish.ExitCode.Should().Be(0, publish.Output);
            publish.Output.Should().NotContain("warning IL2026");
            publish.Output.Should().NotContain("warning IL3050");

            var executable = Path.Combine(output, "CrestCreates.Runtime.Persistence.PostgreSql.AotHost");
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            var execution = await RunScenarioAsync();
            execution.ExitCode.Should().Be(0, execution.Output);
            execution.Output.Should().Contain("CRESTCREATES_AGENT_EXECUTION_PERSISTENCE_NATIVE_OK");
            execution.Output.Should().Contain("CRESTCREATES_AGENT_ORCHESTRATION_NATIVE_OK");
            foreach (var sentinel in new[]
                     {
                         "CRESTCREATES_AGENT_MODE_REACT_NATIVE_OK",
                         "CRESTCREATES_AGENT_MODE_PLAN_AND_EXECUTE_NATIVE_OK",
                         "CRESTCREATES_AGENT_MODE_SEQUENTIAL_NATIVE_OK",
                         "CRESTCREATES_AGENT_MODE_PARALLEL_NATIVE_OK",
                         "CRESTCREATES_AGENT_MODE_SUPERVISOR_NATIVE_OK",
                         "CRESTCREATES_AGENT_MODE_HANDOFF_NATIVE_OK",
                         "CRESTCREATES_AGENT_MODE_GROUP_CHAT_NATIVE_OK",
                         "CRESTCREATES_AGENT_MODE_HUMAN_IN_THE_LOOP_NATIVE_OK",
                         "CRESTCREATES_AGENT_REPLAY_PLAN_NATIVE_OK",
                         "CRESTCREATES_AGENT_REPLAY_PARALLEL_NATIVE_OK",
                         "CRESTCREATES_AGENT_REPLAY_HANDOFF_NATIVE_OK",
                         "CRESTCREATES_AGENT_REPLAY_HUMAN_IN_THE_LOOP_NATIVE_OK",
                         "CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_OLD_EPOCH_OK",
                         "CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_CROSS_TENANT_EVENT_OK",
                         "CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_DUPLICATE_DISPATCH_OK",
                         "CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_LATE_RESULT_OK",
                         "CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_INVALID_JSON_IDENTITY_OK",
                         "CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_INVALID_JSON_REVISION_OK",
                         "CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_UNKNOWN_SELECTOR_OK",
                         "CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_CONTROLS_OK",
                         "CRESTCREATES_AGENT_CRASH_HANDOFF_ACCEPT_OK",
                         "CRESTCREATES_AGENT_CRASH_EVENT_RESUME_OK",
                         "CRESTCREATES_AGENT_CRASH_PARALLEL_CLAIM_OK",
                         "CRESTCREATES_AGENT_ORCHESTRATION_CRASH_WINDOWS_OK"
                     })
            {
                execution.Output.Should().Contain(sentinel, execution.Output);
            }
            execution.Output.Should().Contain("PHASE9B_POSTGRES_SUSPENSION_OK");
            execution.Output.Should().Contain("PHASE9B_POSTGRES_STATE_OK");
            execution.Output.Should().Contain("PHASE9B_POSTGRES_PIN_RECOVERY_OK");
            execution.Output.Should().Contain("PHASE9B_POSTGRES_AUDIT_RETRY_OK");
            execution.Output.Should().Contain("PHASE9C_POSTGRES_OUTBOX_AOT_OK");
            execution.Output.Should().Contain("PHASE9C_POSTGRES_HUMANTASK_DISPATCH_AOT_OK");
            execution.Output.Should().Contain("CRESTCREATES_WORKFLOW_CONTINUATION_RESUME_NATIVE_OK");
            execution.Output.Should().Contain("CRESTCREATES_RUNTIME_OUTBOX_OK");
            execution.Output.Should().Contain("CRESTCREATES_HUMANTASK_RELIABLE_DELIVERY_OK");
            execution.Output.Should().Contain("CRESTCREATES_POSTGRES_AUDIT_SINK_DUPLICATE_AFTER_RESTART_OK");
            execution.Output.Should().Contain("CRESTCREATES_WORKFLOW_ACCOUNTABILITY_RESTART_DUPLICATE_OK");
            execution.Output.Should().Contain("CRESTCREATES_OUTBOX_TERMINAL_ALREADY_APPLIED_OK");
            execution.Output.Should().Contain("CRESTCREATES_OUTBOX_TERMINAL_STALE_FENCE_OK");
            execution.Output.Should().Contain("CRESTCREATES_WORKFLOW_ACCOUNTABILITY_DELIVERY_OK");
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
            execution.Output.Should().Contain("CRESTCREATES_VERSIONED_ORGANIZATION_CACHE_OK");
            execution.Output.Should().Contain("CRESTCREATES_WORKFLOW_CONDITION_AOT_OK");
            execution.Output.Should().Contain("CRESTCREATES_AUDIT_RETENTION_NATIVE_TIME_OK");
            execution.Output.Should().Contain("CRESTCREATES_AUDIT_RETENTION_NATIVE_SCOPE_OK");
            execution.Output.Should().Contain("CRESTCREATES_AUDIT_RETENTION_NATIVE_PROTECTION_OK");
            execution.Output.Should().Contain("CRESTCREATES_AUDIT_RETENTION_NATIVE_HOLD_OK");
            execution.Output.Should().Contain("CRESTCREATES_AUDIT_RETENTION_NATIVE_BATCH_OK");
            execution.Output.Should().Contain("CRESTCREATES_AUDIT_RETENTION_NATIVE_SINK_OK");
            execution.Output.Should().Contain("CRESTCREATES_AUDIT_RETENTION_NATIVE_EXPORT_OK");
            execution.Output.Should().Contain("CRESTCREATES_AUDIT_RETENTION_NATIVE_RECOVERY_OK");
            execution.Output.Should().Contain("CRESTCREATES_AUDIT_RETENTION_NATIVE_CAPABILITY_OK");
            execution.Output.Should().Contain("CRESTCREATES_AUDIT_RETENTION_NATIVE_OK");

            // #132 negative controls: the same native binary runs fixture-only mutants of
            // wrong counts, missing hold filtering and failed batch receipts. Each must be
            // detected, exit non-zero, and never emit the aggregate retention sentinel.
            foreach (var control in new[] { "wrong-count", "hold-filter", "receipt-fault" })
            {
                var controlExecution = await RunScenarioAsync("retention-negative", control);
                controlExecution.ExitCode.Should().NotBe(
                    0,
                    $"retention negative control '{control}' must fail closed: {controlExecution.Output}");
                controlExecution.Output.Should().Contain(
                    $"CRESTCREATES_AUDIT_RETENTION_NEGATIVE_CONTROL_CAUGHT {control}",
                    controlExecution.Output);
                controlExecution.Output.Should().NotContain(
                    "CRESTCREATES_AUDIT_RETENTION_NATIVE_OK",
                    $"retention negative control '{control}' must not emit the aggregate sentinel");
            }

            foreach (var control in new[]
                     {
                         "old-epoch", "cross-tenant-event", "duplicate-dispatch", "late-result",
                         "invalid-json-identity", "invalid-json-revision", "unknown-selector"
                     })
            {
                var controlExecution = await RunScenarioAsync("orchestration-negative", control);
                controlExecution.ExitCode.Should().NotBe(0,
                    $"orchestration negative control '{control}' must fail closed: {controlExecution.Output}");
                controlExecution.Output.Should().Contain(
                    $"CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_CAUGHT {control}:",
                    controlExecution.Output);
                controlExecution.Output.Should().NotContain(
                    "CRESTCREATES_AGENT_ORCHESTRATION_NATIVE_OK",
                    $"orchestration negative control '{control}' must not emit the aggregate sentinel");
            }

            Task<(int ExitCode, string Output)> RunScenarioAsync(params string[] arguments)
            {
                var schema = $"itest_{Guid.NewGuid():N}";
                schemas.Add(schema);
                return TestServiceControl.RunProcessAsync(executable,
                    [connectionString, schema, .. arguments], TimeSpan.FromMinutes(5));
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var cleanup = schemas.Select<string, Func<Task>>(schema => () => DropSchemaAsync(connectionString, schema)).ToList();
        cleanup.Add(() =>
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
            return Task.CompletedTask;
        });
        await ExternalTestServices.CleanupAsync(cleanup, failure);
    }

    private static async Task DropSchemaAsync(string connectionString, string schema)
    {
        var cleanupConnection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false, Timeout = 5, CommandTimeout = 10
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var connection = new NpgsqlConnection(cleanupConnection.ConnectionString);
        await connection.OpenAsync(deadline.Token);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;";
        await command.ExecuteNonQueryAsync(deadline.Token);
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
