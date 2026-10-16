using System.Diagnostics;
using System.Text.Json;
using CrestCreates.GeneratedCrud.AotFixture;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace CrestCreates.GeneratedCrud.AotFixture.Tests;

/// <summary>
/// #128 S4 native gate: Release publish(<c>CrestCreatesPublishMode=aot</c>) → native link → run the
/// published executable in gate mode (it serves the real host over loopback and drives the CRUD
/// scenario matrix inside the native process) → per-scenario sentinels + total sentinel → mutation
/// control proving the gate fails and recycles when an expectation breaks → evidence capture. The
/// JIT HTTP/UoW tests remain the fast integration signal and are not a substitute for this gate.
/// </summary>
[Trait("Category", "NativeAotGate")]
public sealed class GeneratedCrudNativeHttpGateTests
{
    private const string Rid = "linux-x64";

    private static readonly string[] ScenarioSentinels =
    [
        "create",
        "get-by-id",
        "get-unknown-answers-404",
        "search-filter-sort-page",
        "search-default-sort",
        "complex-scalar-filter",
        "update-with-stamp",
        "delete-with-if-match",
        "post-delete-get-404",
        "input-rejection",
        "unit-of-work-commit",
        "unit-of-work-rollback",
        "stale-stamp-rejected",
        "commit-survives-response-write-failure",
        "metadata-rejection",
    ];

    private static readonly Lazy<Task<PublishedFixture>> Published = new(PublishFixtureAsync);

    [Fact]
    public async Task Native_gate_runs_the_full_crud_matrix_against_the_published_binary()
    {
        RequireLinuxX64();

        var fixture = await Published.Value;
        var evidence = new EvidenceCollector("generated-crud-native-http", fixture);
        string? failure = null;
        try
        {
            fixture.PublishExitCode.Should().Be(0, fixture.PublishLog);
            fixture.PublishTimedOut.Should().BeFalse("the bounded publish budget must not be exhausted");
            FixtureTaggedIlWarnings(fixture.PublishLog).Should().BeEmpty(
                "the fixture's own projects must not emit AOT/trim warnings");

            var execution = await RunGateAsync(fixture.ExecutablePath, mutation: null);
            evidence.Add("gateExitCode", execution.ExitCode);
            evidence.Add("gateOutput", execution.Output);
            execution.TimedOut.Should().BeFalse("the native gate must terminate by itself");
            execution.ExitCode.Should().Be(0, execution.Output);

            foreach (var scenario in ScenarioSentinels)
            {
                execution.Output.Should().Contain($"{scenario}:PASS",
                    $"scenario '{scenario}' must execute natively");
            }

            execution.Output.Should().NotContain(":FAIL",
                "no scenario may fail in the green run");
            execution.Output.Should().Contain(CrudNativeGate.TotalSentinel,
                "the total sentinel must be printed only after every scenario passed");
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
    public async Task Native_gate_fails_and_recycles_the_process_when_an_expectation_is_wrong()
    {
        RequireLinuxX64();

        var fixture = await Published.Value;
        var evidence = new EvidenceCollector("generated-crud-native-http-negative", fixture);
        string? failure = null;
        try
        {
            fixture.PublishExitCode.Should().Be(0, fixture.PublishLog);

            var execution = await RunGateAsync(fixture.ExecutablePath, mutation: "wrong-expectation");
            evidence.Add("gateExitCode", execution.ExitCode);
            evidence.Add("gateOutput", execution.Output);
            evidence.Add("expectationMode", "deliberately-wrong");

            execution.TimedOut.Should().BeFalse(
                "the mutated gate must terminate by itself instead of hanging");
            execution.ExitCode.Should().NotBe(0,
                "a wrong content expectation must fail the gate instead of producing a green signal");
            execution.Output.Should().Contain("create:FAIL",
                "the broken expectation must surface as the create scenario failure");
            execution.Output.Should().NotContain(CrudNativeGate.TotalSentinel,
                "the total sentinel must never be printed by a failing run");
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
    public async Task Native_gate_fails_when_the_typed_envelope_expectation_is_wrong()
    {
        RequireLinuxX64();

        var fixture = await Published.Value;
        var evidence = new EvidenceCollector("generated-crud-native-http-typed-envelope-negative", fixture);
        string? failure = null;
        try
        {
            fixture.PublishExitCode.Should().Be(0, fixture.PublishLog);

            var execution = await RunGateAsync(fixture.ExecutablePath, mutation: "wrong-typed-envelope");
            evidence.Add("gateExitCode", execution.ExitCode);
            evidence.Add("gateOutput", execution.Output);
            evidence.Add("expectationMode", "legacy-raw-payload");

            execution.TimedOut.Should().BeFalse(
                "the mutated gate must terminate by itself instead of hanging");
            execution.ExitCode.Should().NotBe(0,
                "a wrong typed-envelope expectation must fail the gate instead of producing a green signal");
            execution.Output.Should().Contain("create:FAIL",
                "the broken envelope expectation must surface as the create scenario failure");
            execution.Output.Should().NotContain(CrudNativeGate.TotalSentinel,
                "the total sentinel must never be printed by a failing run");
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

    private static async Task<ProcessOutcome> RunGateAsync(string executable, string? mutation)
    {
        var environment = new Dictionary<string, string>();
        if (mutation is not null)
        {
            environment[CrudNativeGate.MutationVariable] = mutation;
        }

        return await RunProcessAsync(executable, "--crud-gate", TimeSpan.FromMinutes(5), environment);
    }

    private static async Task<PublishedFixture> PublishFixtureAsync()
    {
        var root = FindRepoRoot();
        var outputDirectory = Path.Combine(
            Path.GetTempPath(), "crest-generated-crud-aot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        var project = Path.Combine(
            root,
            "tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture/CrestCreates.GeneratedCrud.AotFixture.csproj");
        var command =
            $"publish \"{project}\" -c Release -r {Rid} --self-contained true " +
            $"-p:CrestCreatesPublishMode=aot --disable-build-servers -o \"{outputDirectory}\"";

        var publish = await RunProcessAsync("dotnet", command, TimeSpan.FromMinutes(25), new Dictionary<string, string>());

        var executable = Path.Combine(outputDirectory, "CrestCreates.GeneratedCrud.AotFixture");
        if (publish.ExitCode == 0 && !publish.TimedOut && !OperatingSystem.IsWindows() && File.Exists(executable))
        {
            File.SetUnixFileMode(
                executable,
                File.GetUnixFileMode(executable)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        return new PublishedFixture(
            OutputDirectory: outputDirectory,
            ExecutablePath: executable,
            Command: "dotnet " + command,
            PublishExitCode: publish.ExitCode,
            PublishTimedOut: publish.TimedOut,
            PublishLog: publish.Output);
    }

    private static IEnumerable<string> FixtureTaggedIlWarnings(string publishLog)
        => publishLog
            .Split('\n')
            .Where(line =>
                (line.Contains("warning IL2026", StringComparison.Ordinal)
                 || line.Contains("warning IL3050", StringComparison.Ordinal))
                // The diagnostic's own source path must be inside the fixture's tree. The trailing
                // [project.csproj] tag belongs to the consuming project, so dependency warnings stay
                // visible in the log without being attributed to the fixture (their subsystem's scope).
                && DiagnosticSourcePath(line).Contains(
                    "tests/Framework/Api/CrestCreates.GeneratedCrud", StringComparison.Ordinal));

    /// <summary>MSBuild diagnostic line shape: '&lt;path&gt;(&lt;line&gt;): ... ' — the path is everything before the first ': '.</summary>
    private static string DiagnosticSourcePath(string line)
    {
        var separator = line.IndexOf(": ", StringComparison.Ordinal);
        return separator > 0 ? line[..separator] : line;
    }

    private static async Task<ProcessOutcome> RunProcessAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        Dictionary<string, string> environment)
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
        foreach (var (key, value) in environment)
        {
            process.StartInfo.Environment[key] = value;
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
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    private static string ResolveSha()
    {
        var sha = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(sha))
            return sha!;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = FindRepoRoot()
            });
            if (process is null)
                return "unknown";
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            return output.Length > 0 ? output : "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    private sealed record ProcessOutcome(int ExitCode, string Output, bool TimedOut);

    private sealed record PublishedFixture(
        string OutputDirectory,
        string ExecutablePath,
        string Command,
        int PublishExitCode,
        bool PublishTimedOut,
        string PublishLog);

    private sealed class EvidenceCollector
    {
        private readonly string _name;
        private readonly Dictionary<string, object?> _fields = new();

        public EvidenceCollector(string name, PublishedFixture fixture)
        {
            _name = name;
            _fields["gate"] = "generated-crud-native-http";
            _fields["sha"] = ResolveSha();
            _fields["rid"] = Rid;
            _fields["publishCommand"] = fixture.Command;
            _fields["publishExitCode"] = fixture.PublishExitCode;
            _fields["publishTimedOut"] = fixture.PublishTimedOut;
            _fields["publishLog"] = fixture.PublishLog;
            _fields["writtenUtc"] = DateTime.UtcNow.ToString("o");
        }

        public void Add(string key, object? value) => _fields[key] = value;

        public void Write(string result, string? failure)
        {
            try
            {
                var directory = Path.Combine(FindRepoRoot(), "tests", "artifacts", "aot-native-evidence");
                Directory.CreateDirectory(directory);
                _fields["result"] = result;
                _fields["failure"] = failure;
                File.WriteAllText(
                    Path.Combine(directory, _name + ".json"),
                    JsonSerializer.Serialize(_fields, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception)
            {
            }
        }
    }
}
