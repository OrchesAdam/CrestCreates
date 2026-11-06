using System.Diagnostics;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace CrestCreates.Core.AotHost.Tests;

/// <summary>
/// Dedicated Core native gate: Release publish(<c>CrestCreatesPublishMode=aot</c>) → native link →
/// execute the published binary against the semantic-identity / JSON-converter / cancellation-snapshot
/// scenario matrix → per-scenario sentinels plus the total sentinel. The missing-argument run is a
/// negative control proving the sentinel is not emitted unconditionally.
/// </summary>
[Trait("Category", "NativeAotGate")]
public sealed class CoreNativeGateTests
{
    private const string Rid = "linux-x64";

    private static readonly string[] ScenarioSentinels =
    [
        "CORE_SCENARIO_OK:semantic-ids",
        "CORE_SCENARIO_OK:severity-ranking",
        "CORE_SCENARIO_OK:json-roundtrip",
        "CORE_SCENARIO_OK:json-rejects-unknown-severity",
        "CORE_SCENARIO_OK:json-rejects-empty-diagnostic",
        "CORE_SCENARIO_OK:cancellation-snapshot",
    ];

    private const string TotalSentinel = "CRESTCREATES_CORE_SAMPLE_OK";

    private static readonly Lazy<Task<PublishedFixture>> Published = new(PublishFixtureAsync);

    [Fact]
    public async Task Native_core_binary_runs_the_identity_and_snapshot_matrix()
    {
        RequireLinuxX64();

        var fixture = await Published.Value;
        fixture.PublishExitCode.Should().Be(0, fixture.PublishLog);
        fixture.PublishTimedOut.Should().BeFalse("the bounded publish budget must not be exhausted");
        FixtureTaggedIlWarnings(fixture.PublishLog).Should().BeEmpty(
            "the Core fixture's own projects must not emit AOT/trim warnings");

        var execution = await RunAsync(fixture.ExecutablePath, "--golden-scenario");

        execution.ExitCode.Should().Be(0, execution.Output);
        foreach (var sentinel in ScenarioSentinels)
        {
            execution.Output.Should().Contain(sentinel);
        }

        execution.Output.Should().Contain(TotalSentinel);
    }

    [Fact]
    public async Task Native_core_binary_without_the_scenario_argument_refuses_and_emits_no_sentinel()
    {
        RequireLinuxX64();

        var fixture = await Published.Value;
        fixture.PublishExitCode.Should().Be(0, fixture.PublishLog);
        FixtureTaggedIlWarnings(fixture.PublishLog).Should().BeEmpty();

        var execution = await RunAsync(fixture.ExecutablePath, arguments: null);

        execution.ExitCode.Should().Be(2, execution.Output);
        execution.Output.Should().NotContain(TotalSentinel,
            "the sentinel must only appear after a successful scenario run");
    }

    private static async Task<PublishedFixture> PublishFixtureAsync()
    {
        var root = FindRepoRoot();
        var outputDirectory = Path.Combine(
            Path.GetTempPath(), "crest-core-aot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        var project = Path.Combine(
            root, "tests/Core/CrestCreates.Core.AotHost/CrestCreates.Core.AotHost.csproj");
        var command =
            $"publish \"{project}\" -c Release -r {Rid} --self-contained true " +
            $"-p:CrestCreatesPublishMode=aot --disable-build-servers -o \"{outputDirectory}\"";

        var publish = await RunProcessAsync(DotnetMuxer(), command, TimeSpan.FromMinutes(15));

        var executable = Path.Combine(outputDirectory, "CrestCreates.Core.AotHost");
        if (publish.ExitCode == 0 && !publish.TimedOut && !OperatingSystem.IsWindows() && File.Exists(executable))
        {
            File.SetUnixFileMode(
                executable,
                File.GetUnixFileMode(executable)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        return new PublishedFixture(outputDirectory, executable, publish.ExitCode, publish.TimedOut, publish.Output);
    }

    private static Task<ProcessOutcome> RunAsync(string executable, string? arguments)
        => RunProcessAsync(executable, arguments ?? string.Empty, TimeSpan.FromMinutes(2));

    /// <summary>
    /// A bare "dotnet" resolves the system muxer when the test runner is started without the
    /// user-local SDK on PATH; honour the host path VSTest/MSBuild may hand down first.
    /// </summary>
    private static string DotnetMuxer()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return string.IsNullOrWhiteSpace(hostPath) ? "dotnet" : hostPath;
    }

    private static IEnumerable<string> FixtureTaggedIlWarnings(string publishLog)
        => publishLog
            .Split('\n')
            .Where(line =>
                (line.Contains("warning IL2026", StringComparison.Ordinal)
                 || line.Contains("warning IL3050", StringComparison.Ordinal))
                && DiagnosticSourcePath(line).Contains(
                    "tests/Core/CrestCreates.Core.AotHost", StringComparison.Ordinal));

    private static string DiagnosticSourcePath(string line)
    {
        var separator = line.IndexOf(": ", StringComparison.Ordinal);
        return separator > 0 ? line[..separator] : line;
    }

    private static async Task<ProcessOutcome> RunProcessAsync(
        string fileName, string arguments, TimeSpan timeout)
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
        using var timeoutSource = new CancellationTokenSource(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        var output = await stdout + await stderr;
        return new ProcessOutcome(timedOut ? -1 : process.ExitCode, output, timedOut);
    }

    private static void RequireLinuxX64()
    {
        if (!OperatingSystem.IsLinux()
            || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                != System.Runtime.InteropServices.Architecture.X64)
        {
            throw SkipException.ForSkip($"The native gate is pinned to {Rid}.");
        }
    }

    private static string FindRepoRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "CrestCreates.slnx")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the CrestCreates repository root.");
    }

    private sealed record PublishedFixture(
        string OutputDirectory,
        string ExecutablePath,
        int PublishExitCode,
        bool PublishTimedOut,
        string PublishLog);

    private sealed record ProcessOutcome(int ExitCode, string Output, bool TimedOut);
}
