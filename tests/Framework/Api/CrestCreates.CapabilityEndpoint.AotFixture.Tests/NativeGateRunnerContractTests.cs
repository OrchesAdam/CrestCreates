using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace CrestCreates.CapabilityEndpoint.AotFixture.Tests;

/// <summary>
/// Bounded contract tests for the native gate runner itself: startup failure,
/// readiness timeout and process reclamation must propagate as failures, and a
/// missing executable must never produce a green signal. These tests never
/// publish; they prove failure propagation with fast, reproducible stubs.
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
