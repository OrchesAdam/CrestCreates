using System.Diagnostics;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace CrestCreates.CapabilityEndpoint.AotFixture.Tests;

/// <summary>
/// Bounded contract tests for the native gate runner itself: startup failure,
/// readiness timeout, listening-line mismatch, publish/process timeouts and
/// process reclamation must propagate as failures, and a missing executable
/// must never produce a green signal. These tests never publish a real
/// fixture; they prove failure propagation and cleanup with fast,
/// reproducible stubs through the same helpers the gate uses.
/// </summary>
[Trait("Category", "NativeAotGate")]
public sealed class NativeGateRunnerContractTests
{
    [Fact]
    public void Missing_executable_fails_before_launch()
    {
        RequireLinuxX64();

        var missing = Path.Combine(Path.GetTempPath(), "crest-missing-" + Guid.NewGuid().ToString("N"));

        var act = () => NativeServerProcess.Start(missing, Path.GetTempPath(), "http://127.0.0.1:1");

        act.Should().Throw<FileNotFoundException>()
            .Which.Message.Should().Contain(missing,
                "a missing fixture executable must fail explicitly instead of skipping the gate");
    }

    [Fact]
    public async Task Early_exit_reports_exit_code_and_reaps_process()
    {
        RequireLinuxX64();

        var server = NativeServerProcess.Start(
            "/bin/bash", Path.GetTempPath(), new[] { "-c", "exit 7" });
        try
        {
            var act = async () => await NativeGateWaiter.WaitForListeningLineAsync(
                server, TimeSpan.FromSeconds(15));

            var exception = await act.Should().ThrowAsync<NativeFixtureStartupException>(
                "a process that exits before readiness must fail the gate");
            exception.Which.Message.Should().Contain("7");
        }
        finally
        {
            var exitCode = await server.ReapAsync();
            server.HasExited.Should().BeTrue("the runner must reap the process it started");
            exitCode.Should().NotBeNull();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Readiness_timeout_kills_process_instead_of_waiting_forever()
    {
        RequireLinuxX64();

        var server = NativeServerProcess.Start(
            "/bin/bash", Path.GetTempPath(), new[] { "-c", "sleep 30" });
        try
        {
            using var http = new HttpClient
            {
                BaseAddress = new Uri("http://127.0.0.1:1"),
                Timeout = TimeSpan.FromSeconds(2)
            };
            var act = async () => await NativeGateWaiter.WaitForHttpReadyAsync(
                server, http, "/", TimeSpan.FromSeconds(1.5));

            var exception = await act.Should().ThrowAsync<TimeoutException>(
                "a process that never serves HTTP must hit the bounded readiness deadline");
            exception.Which.Message.Should().Contain("00:00:01.5");
        }
        finally
        {
            var exitCode = await server.ReapAsync();
            server.HasExited.Should().BeTrue(
                "the runner must kill the process tree when readiness times out");
            exitCode.Should().NotBeNull();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Startup_helper_reaps_child_when_listen_wait_times_out()
    {
        RequireLinuxX64();

        var act = async () => await NativeServerLauncher.StartWithBoundedBindRetryAsync(
            "/bin/bash",
            Path.GetTempPath(),
            listenTimeout: TimeSpan.FromSeconds(1.5),
            maxAttempts: 1,
            runArguments: new[] { "-c", "echo starting; sleep 30" });

        var exception = await act.Should().ThrowAsync<NativeFixtureStartupException>(
            "a child that never reports its listening endpoint must fail the bounded startup");
        exception.Which.Message.Should().Contain("attempt 1/1");
        exception.Which.Message.Should().Contain("did not report a listening endpoint");
        exception.Which.ProcessExitCode.Should().NotBeNull(
            "the startup helper must reap the child before rethrowing");
        exception.Which.StandardOutput.Should().Contain("starting",
            "captured output must be preserved on the failure path");

        AssertProcessGone(exception.Which.ProcessId,
            "the child must be gone after the listen-wait timeout");
    }

    [Fact]
    public async Task Startup_helper_reaps_child_when_listening_line_does_not_match()
    {
        RequireLinuxX64();

        var act = async () => await NativeServerLauncher.StartWithBoundedBindRetryAsync(
            "/bin/bash",
            Path.GetTempPath(),
            listenTimeout: TimeSpan.FromSeconds(15),
            maxAttempts: 1,
            runArguments: new[] { "-c", "echo 'Now listening on: http://127.0.0.1:1'; sleep 30" });

        var exception = await act.Should().ThrowAsync<NativeFixtureStartupException>(
            "a listening line from a different endpoint must fail closed");
        exception.Which.Message.Should().Contain("attempt 1/1");
        exception.Which.ProcessExitCode.Should().NotBeNull(
            "the startup helper must reap the child before rethrowing");
        exception.Which.StandardOutput.Should().Contain("Now listening on: http://127.0.0.1:1");

        AssertProcessGone(exception.Which.ProcessId,
            "the child must be gone after the listening-line mismatch");
    }

    [Fact]
    public async Task RunProcess_timeout_preserves_partial_output_and_reaps_child()
    {
        RequireLinuxX64();

        var result = await NativeAotGateSupport.RunProcessAsync(
            "/bin/bash", "-c \"echo partial-transcript; sleep 30\"", TimeSpan.FromSeconds(1.5));

        result.TimedOut.Should().BeTrue();
        result.ExitCode.Should().Be(-1);
        result.Output.Should().Contain("partial-transcript",
            "the transcript captured before the timeout must not be discarded");
        result.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));

        AssertProcessGone(result.ProcessId,
            "the timed-out process must be reaped instead of being left behind");
    }

    private static void AssertProcessGone(int? processId, string because)
    {
        processId.Should().NotBeNull();
        var probe = () => Process.GetProcessById(processId!.Value);
        probe.Should().Throw<ArgumentException>(because);
    }

    private static void RequireLinuxX64()
    {
        if (!OperatingSystem.IsLinux()
            || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                != System.Runtime.InteropServices.Architecture.X64)
        {
            throw SkipException.ForSkip("The native gate runner contract is pinned to linux-x64.");
        }
    }
}
