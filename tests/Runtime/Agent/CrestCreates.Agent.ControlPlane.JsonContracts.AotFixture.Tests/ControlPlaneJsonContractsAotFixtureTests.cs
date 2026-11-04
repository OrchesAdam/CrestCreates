using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.Tests;

/// <summary>
/// Issue #159 S3 native gate. One linux-x64 <c>PublishAot</c> of the Control Plane JsonContracts
/// fixture is shared by the class: the green run name-checks every scenario sentinel instead of
/// trusting the total sentinel, and every gate negative control runs the same binary separately, where
/// the execution must exit non-zero and withhold the total sentinel. A correct rejection (an invalid
/// provider answer the fixture asserts) is a green scenario; only a broken expectation may turn the
/// gate red, so the two layers are never judged in one execution.
/// </summary>
public sealed class ControlPlaneJsonContractsAotFixtureTests
{
    private const string TotalSentinel = "CONTROL_PLANE_JSON_CONTRACT_NATIVEAOT_OK";
    private const string AggregateSentinel = "SerializeDeserialize_RepresentativeToolRoots";

    private static readonly string[] ScenarioSentinels =
    [
        "ReflectionFallback_IsDisabled",
        "AgentDraftIdentityBinding",
        "ReviewHashInputSourceGeneratedRoundTrip",
        "AgentAuthoringOptionalReferences",
        "AgentAuthoringUpdateBaseVersion",
        "AgentAuthoringOutcomeRequiredRoots",
        "AgentAuthoringUnsupportedOutcome",
        "AgentAuthoringClarificationOutcome",
        "AgentAuthoringOutcomeShapeConflict",
        "AgentAuthoringCodeTableRejection",
        "AgentAuthoringEnvelopeUnknownMember",
        "AgentAuthoringEnvelopeDuplicatePrecedence",
        "AgentAuthoringWireDisclosure",
    ];

    /// <summary>
    /// Each control asserts the opposite of one contract rule, so exactly the scenario owning that rule
    /// may redden. A control that reddens a second scenario, or none, proves nothing about the rule it
    /// names, which is why the pairs are pinned here rather than derived from the output.
    /// </summary>
    public static TheoryData<string, string> GateNegativeControls => new()
    {
        { "unsupported-outcome-dropped", "AgentAuthoringUnsupportedOutcome" },
        { "clarification-outcome-dropped", "AgentAuthoringClarificationOutcome" },
        { "outcome-shape-accepted", "AgentAuthoringOutcomeShapeConflict" },
        { "code-table-accepted", "AgentAuthoringCodeTableRejection" },
        { "unknown-member-accepted", "AgentAuthoringEnvelopeUnknownMember" },
        { "duplicate-precedence-blocked", "AgentAuthoringEnvelopeDuplicatePrecedence" },
        { "wire-disclosure-outcome-accepted", "AgentAuthoringWireDisclosure" },
    };

    private static readonly Lazy<Task<PublishedFixture>> Published = new(PublishFixtureAsync);

    [Fact]
    public async Task Publish_native_aot_control_plane_json_contract_fixture_links_and_runs()
    {
        RequireLinuxX64();
        var fixture = await Published.Value;

        fixture.PublishExitCode.Should().Be(0, fixture.PublishOutput);
        fixture.PublishOutput.Should().NotContain("warning IL2026", fixture.PublishOutput);
        fixture.PublishOutput.Should().NotContain("warning IL3050", fixture.PublishOutput);
        fixture.DllNames.Should().NotContain(name =>
            name.Contains("JsonContracts.BuildTasks", StringComparison.Ordinal)
            || name.Contains("JsonContracts.Tool", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));

        var execution = await RunAsync(fixture.ExecutablePath, string.Empty, TimeSpan.FromMinutes(2));
        await File.WriteAllTextAsync(Path.Combine(fixture.OutputDirectory, "run.log"), execution.Output);
        execution.ExitCode.Should().Be(0, execution.Output);

        // The total sentinel alone hides a scenario that never ran or that the harness forgot to name.
        foreach (var scenario in ScenarioSentinels)
        {
            execution.Output.Should().Contain($"{scenario}:PASS",
                $"the green run must attest the {scenario} scenario by name");
        }

        execution.Output.Should().Contain($"{AggregateSentinel}:PASS", execution.Output);
        execution.Output.Should().NotContain(":FAIL", execution.Output);
        execution.Output.Should().Contain("CONTROL_PLANE_LOCALIZED_MESSAGE_NATIVEAOT_OK");
        execution.Output.Should().Contain("ActivationReviewCallbackBoundary:PASS");
        execution.Output.Should().Contain("CONTROL_PLANE_REVIEW_HASH_INPUT_NATIVEAOT_OK");
        execution.Output.Should().Contain("CONTROL_PLANE_PROJECTION_SCOPE_NATIVEAOT_OK");
        execution.Output.Should().Contain("CONTROL_PLANE_PACKAGE_EVIDENCE_ARTIFACT_MEMORY_NATIVEAOT_OK");
        execution.Output.Should().Contain("PackageEvidenceArtifactGeneratedEnvelopeAndOfficialSerializer:PASS");
        execution.Output.Should().Contain("CONTROL_PLANE_REVIEW_ARTIFACT_STORE_MEMORY_NATIVEAOT_OK");
        execution.Output.Should().Contain("CONTROL_PLANE_REPORT_INPUT_NATIVEAOT_OK");
        execution.Output.Should().Contain("CONTROL_PLANE_AGENT_AUTHORING_OUTCOME_NATIVEAOT_OK");
        execution.Output.Should().Contain(TotalSentinel);
    }

    [Theory]
    [MemberData(nameof(GateNegativeControls))]
    public async Task GateNegativeControl_fails_only_its_own_scenario_and_withholds_the_total_sentinel(
        string control,
        string expectedFailedScenario)
    {
        RequireLinuxX64();
        var fixture = await Published.Value;
        fixture.PublishExitCode.Should().Be(0, fixture.PublishOutput);

        var execution = await RunAsync(
            fixture.ExecutablePath,
            $"--authoring-gate-control={control}",
            TimeSpan.FromMinutes(2));
        await File.WriteAllTextAsync(
            Path.Combine(fixture.OutputDirectory, $"run-{control}.log"), execution.Output);

        execution.ExitCode.Should().NotBe(0,
            $"the {control} control asserts a contract rule that holds, so the gate must fail");
        execution.Output.Should().Contain($"{expectedFailedScenario}:FAIL", execution.Output);
        execution.Output.Should().NotContain(TotalSentinel);
        execution.Output.Should().NotContain("CONTROL_PLANE_AGENT_AUTHORING_OUTCOME_NATIVEAOT_OK");

        var reddened = ScenarioSentinels
            .Where(scenario => execution.Output.Contains($"{scenario}:FAIL", StringComparison.Ordinal))
            .ToArray();
        // The aggregate line follows any failing scenario by construction; the isolation claim is about
        // the named scenarios, so it is compared against those alone.
        reddened.Should().BeEquivalentTo(
            new[] { expectedFailedScenario },
            "no scenario other than {0} may redden for this control", expectedFailedScenario);
    }

    [Fact]
    public async Task UnknownGateControl_is_refused_before_any_scenario_runs()
    {
        RequireLinuxX64();
        var fixture = await Published.Value;

        var execution = await RunAsync(
            fixture.ExecutablePath, "--authoring-gate-control=typo-control", TimeSpan.FromMinutes(2));

        execution.ExitCode.Should().NotBe(0, execution.Output);
        execution.Output.Should().NotContain(TotalSentinel);
        // A mistyped control must not be silently dropped into the green configuration.
        execution.Output.Should().NotContain("ReflectionFallback_IsDisabled:PASS", execution.Output);
    }

    private static async Task<PublishedFixture> PublishFixtureAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var output = Path.Combine(
            repositoryRoot,
            "artifacts",
            "control-plane-json-aot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);

        var project = Path.Combine(
            repositoryRoot,
            "tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.csproj");
        var publish = await RunAsync(
            "dotnet",
            $"publish \"{project}\" -c Release -r linux-x64 --self-contained true "
            + $"-p:CrestCreatesPublishMode=aot --disable-build-servers -o \"{output}\"",
            TimeSpan.FromMinutes(20));
        await File.WriteAllTextAsync(Path.Combine(output, "publish.log"), publish.Output);

        var executable = Path.Combine(output, "CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture");
        if (publish.ExitCode == 0 && File.Exists(executable))
        {
            File.SetUnixFileMode(
                executable,
                File.GetUnixFileMode(executable)
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherExecute);
        }

        var dllNames = File.Exists(executable)
            ? Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetFileName(path)!)
                .ToArray()
            : Array.Empty<string>();

        return new PublishedFixture(output, executable, publish.ExitCode, publish.Output, dllNames);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout)
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

    private static void RequireLinuxX64()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw Xunit.Sdk.SkipException.ForSkip("The Control Plane JsonContracts NativeAOT gate is pinned to linux-x64.");
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

    private sealed record PublishedFixture(
        string OutputDirectory,
        string ExecutablePath,
        int PublishExitCode,
        string PublishOutput,
        IReadOnlyList<string> DllNames);
}
