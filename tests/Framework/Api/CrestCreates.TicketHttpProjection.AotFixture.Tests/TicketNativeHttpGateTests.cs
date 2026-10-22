using System.Diagnostics;
using System.Text.Json;
using CrestCreates.TicketHttpProjection.AotFixture;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace CrestCreates.TicketHttpProjection.AotFixture.Tests;

/// <summary>
/// S5 native gate: Release publish(<c>CrestCreatesPublishMode=aot</c>) → native link → run the
/// published Ticket fixture in gate mode (real generated HTTP bundle + real TicketAppService over the
/// memory persistence leaf) → per-scenario sentinels + total sentinel → two mutation controls and
/// evidence capture. The JIT tests are not a substitute for this gate.
/// </summary>
[Trait("Category", "NativeAotGate")]
public sealed class TicketNativeHttpGateTests
{
    private const string Rid = "linux-x64";

    private static readonly string[] ScenarioSentinels =
    [
        "create",
        "get-by-id",
        "get-unknown-answers-404",
        "list-all",
        "list-by-customer",
        "update-with-stamp",
        "assign-with-active-sla",
        "list-by-assignee",
        "resolve",
        "close",
        "business-failure-is-serializable",
        "delete-with-if-match",
        "invalid-body-zero-business",
        "permission-denied",
        "metadata-rejection",
    ];

    private static readonly Lazy<Task<PublishedFixture>> Published = new(PublishFixtureAsync);

    [Fact]
    public async Task Native_gate_runs_the_ticket_matrix_against_the_published_binary()
    {
        RequireLinuxX64();
        var fixture = await Published.Value;
        var evidence = new EvidenceCollector("ticket-native-http", fixture);
        string? failure = null;
        try
        {
            fixture.PublishExitCode.Should().Be(0, fixture.PublishLog);
            fixture.PublishTimedOut.Should().BeFalse();
            FixtureTaggedIlWarnings(fixture.PublishLog).Should().BeEmpty(
                "the Ticket fixture's own projects must not emit AOT/trim warnings");

            var execution = await RunGateAsync(fixture.ExecutablePath, mutation: null);
            evidence.Add("gateExitCode", execution.ExitCode);
            evidence.Add("gateOutput", execution.Output);
            execution.TimedOut.Should().BeFalse("the native gate must terminate by itself");
            execution.ExitCode.Should().Be(0, execution.Output);
            foreach (var scenario in ScenarioSentinels)
            {
                execution.Output.Should().Contain($"{scenario}:PASS");
            }

            execution.Output.Should().NotContain(":FAIL");
            execution.Output.Should().Contain(TicketNativeGate.TotalSentinel);
        }
        catch (Exception exception)
        {
            failure = $"{exception.GetType().Name}: {exception.Message}";
            throw;
        }
        finally
        {
            evidence.Write(failure is null ? "passed" : "failed", failure);
        }
    }

    [Fact]
    public async Task Wrong_expectation_mutation_fails_and_recycles()
    {
        RequireLinuxX64();
        var fixture = await Published.Value;
        var evidence = new EvidenceCollector("ticket-native-http-wrong-expectation", fixture);
        string? failure = null;
        try
        {
            fixture.PublishExitCode.Should().Be(0, fixture.PublishLog);
            var execution = await RunGateAsync(fixture.ExecutablePath, "wrong-expectation");
            evidence.Add("gateExitCode", execution.ExitCode);
            evidence.Add("gateOutput", execution.Output);
            execution.TimedOut.Should().BeFalse();
            execution.ExitCode.Should().NotBe(0);
            execution.Output.Should().Contain("create:FAIL");
            execution.Output.Should().NotContain(TicketNativeGate.TotalSentinel);
        }
        catch (Exception exception)
        {
            failure = $"{exception.GetType().Name}: {exception.Message}";
            throw;
        }
        finally
        {
            evidence.Write(failure is null ? "passed" : "failed", failure);
        }
    }

    [Fact]
    public async Task Wrong_validation_status_mutation_fails_and_recycles()
    {
        RequireLinuxX64();
        var fixture = await Published.Value;
        var evidence = new EvidenceCollector("ticket-native-http-wrong-validation", fixture);
        string? failure = null;
        try
        {
            fixture.PublishExitCode.Should().Be(0, fixture.PublishLog);
            var execution = await RunGateAsync(fixture.ExecutablePath, "wrong-validation-status");
            evidence.Add("gateExitCode", execution.ExitCode);
            evidence.Add("gateOutput", execution.Output);
            execution.TimedOut.Should().BeFalse();
            execution.ExitCode.Should().NotBe(0);
            execution.Output.Should().Contain("invalid-body-zero-business:FAIL");
            execution.Output.Should().NotContain(TicketNativeGate.TotalSentinel);
        }
        catch (Exception exception)
        {
            failure = $"{exception.GetType().Name}: {exception.Message}";
            throw;
        }
        finally
        {
            evidence.Write(failure is null ? "passed" : "failed", failure);
        }
    }

    [Fact]
    public async Task Late_validation_mutation_fails_the_zero_business_scenario()
    {
        RequireLinuxX64();
        var fixture = await Published.Value;
        var evidence = new EvidenceCollector("ticket-native-http-late-validation", fixture);
        string? failure = null;
        try
        {
            fixture.PublishExitCode.Should().Be(0, fixture.PublishLog);
            var execution = await RunGateAsync(
                fixture.ExecutablePath,
                mutation: null,
                environment: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["CREST_TICKET_GATE_MUTATION"] = "late-validation",
                });
            evidence.Add("gateExitCode", execution.ExitCode);
            evidence.Add("gateOutput", execution.Output);
            execution.TimedOut.Should().BeFalse();
            execution.ExitCode.Should().NotBe(0);
            execution.Output.Should().Contain("invalid-body-zero-business:FAIL");
            execution.Output.Should().NotContain(TicketNativeGate.TotalSentinel);
        }
        catch (Exception exception)
        {
            failure = $"{exception.GetType().Name}: {exception.Message}";
            throw;
        }
        finally
        {
            evidence.Write(failure is null ? "passed" : "failed", failure);
        }
    }

    [Fact]
    public async Task Decoy_metadata_contribution_fails_the_owner_lock()
    {
        RequireLinuxX64();
        var fixture = await Published.Value;
        var evidence = new EvidenceCollector("ticket-native-http-decoy-metadata", fixture);
        string? failure = null;
        try
        {
            fixture.PublishExitCode.Should().Be(0, fixture.PublishLog);
            var execution = await RunGateAsync(fixture.ExecutablePath, "decoy-metadata");
            evidence.Add("gateExitCode", execution.ExitCode);
            evidence.Add("gateOutput", execution.Output);
            execution.TimedOut.Should().BeFalse();
            execution.ExitCode.Should().NotBe(0);
            execution.Output.Should().Contain("metadata-rejection:FAIL");
            execution.Output.Should().NotContain(TicketNativeGate.TotalSentinel);
        }
        catch (Exception exception)
        {
            failure = $"{exception.GetType().Name}: {exception.Message}";
            throw;
        }
        finally
        {
            evidence.Write(failure is null ? "passed" : "failed", failure);
        }
    }

    private static Task<ProcessOutcome> RunGateAsync(
        string executable,
        string? mutation,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var arguments = mutation is null ? "--ticket-gate" : $"--ticket-gate --ticket-mutation={mutation}";
        return RunProcessAsync(executable, arguments, TimeSpan.FromMinutes(5), environment);
    }

    private static async Task<PublishedFixture> PublishFixtureAsync()
    {
        var root = FindRepoRoot();
        var outputDirectory = Path.Combine(Path.GetTempPath(), "crest-ticket-aot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        var project = Path.Combine(
            root,
            "tests/Framework/Api/CrestCreates.TicketHttpProjection.AotFixture/CrestCreates.TicketHttpProjection.AotFixture.csproj");
        var command =
            $"publish \"{project}\" -c Release -r {Rid} --self-contained true " +
            $"-p:CrestCreatesPublishMode=aot --disable-build-servers -o \"{outputDirectory}\"";
        var publish = await RunProcessAsync("dotnet", command, TimeSpan.FromMinutes(25));
        var executable = Path.Combine(outputDirectory, "CrestCreates.TicketHttpProjection.AotFixture");
        if (publish.ExitCode == 0 && !publish.TimedOut && File.Exists(executable))
        {
            File.SetUnixFileMode(
                executable,
                File.GetUnixFileMode(executable)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        return new PublishedFixture(
            outputDirectory,
            executable,
            "dotnet " + command,
            publish.ExitCode,
            publish.TimedOut,
            publish.Output);
    }

    private static IEnumerable<string> FixtureTaggedIlWarnings(string publishLog)
        => publishLog.Split('\n').Where(line =>
            (line.Contains("warning IL2026", StringComparison.Ordinal)
             || line.Contains("warning IL3050", StringComparison.Ordinal))
            && DiagnosticSourcePath(line).Contains(
                "tests/Framework/Api/CrestCreates.TicketHttpProjection.AotFixture", StringComparison.Ordinal));

    private static string DiagnosticSourcePath(string line)
    {
        var separator = line.IndexOf(": ", StringComparison.Ordinal);
        return separator > 0 ? line[..separator] : line;
    }

    private static async Task<ProcessOutcome> RunProcessAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        };
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                process.StartInfo.Environment[key] = value;
            }
        }

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

        return new ProcessOutcome(timedOut ? -1 : process.ExitCode, await stdout + await stderr, timedOut);
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
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CrestCreates.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    private sealed record ProcessOutcome(int ExitCode, string Output, bool TimedOut);

    private sealed record PublishedFixture(
        string OutputDirectory,
        string ExecutablePath,
        string Command,
        int PublishExitCode,
        bool PublishTimedOut,
        string PublishLog);

    private sealed class EvidenceCollector(string gate, PublishedFixture fixture)
    {
        private readonly Dictionary<string, object?> _fields = new(StringComparer.Ordinal);

        public void Add(string key, object? value) => _fields[key] = value;

        public void Write(string result, string? failure)
        {
            var root = FindRepoRoot();
            var directory = Path.Combine(root, "tests/artifacts/aot-native-evidence");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, gate + ".json");
            var evidence = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["gate"] = gate,
                ["sha"] = ResolveSha(root),
                ["rid"] = Rid,
                ["publishCommand"] = fixture.Command,
                ["publishExitCode"] = fixture.PublishExitCode,
                ["publishTimedOut"] = fixture.PublishTimedOut,
                ["publishLog"] = fixture.PublishLog,
                ["writtenUtc"] = DateTimeOffset.UtcNow,
                ["result"] = result,
                ["failure"] = failure,
            };
            foreach (var (key, value) in _fields)
            {
                evidence[key] = value;
            }

            File.WriteAllText(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string ResolveSha(string root)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse HEAD",
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                })!;
                var sha = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return sha;
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
