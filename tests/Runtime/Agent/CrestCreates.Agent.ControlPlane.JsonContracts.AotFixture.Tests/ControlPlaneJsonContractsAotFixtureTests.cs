using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.Tests;

public sealed class ControlPlaneJsonContractsAotFixtureTests
{
    [Fact]
    public async Task Publish_native_aot_control_plane_json_contract_fixture_links_and_runs()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw Xunit.Sdk.SkipException.ForSkip("The Control Plane JsonContracts NativeAOT gate is pinned to linux-x64.");

        var repositoryRoot = FindRepositoryRoot();
        var output = Path.Combine(Path.GetTempPath(), "crest-control-plane-json-aot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);

        try
        {
            var project = Path.Combine(
                repositoryRoot,
                "tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.csproj");
            var publish = await RunAsync(
                "dotnet",
                $"publish \"{project}\" -c Release -r linux-x64 --self-contained true -p:CrestCreatesPublishMode=aot --disable-build-servers -o \"{output}\"",
                TimeSpan.FromMinutes(5));

            publish.ExitCode.Should().Be(0, publish.Output);
            publish.Output.Should().NotContain("warning IL2026");
            publish.Output.Should().NotContain("warning IL3050");
            Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Should().NotContain(name =>
                    name!.Contains("JsonContracts.BuildTasks", StringComparison.Ordinal)
                    || name.Contains("JsonContracts.Tool", StringComparison.Ordinal)
                    || name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));

            var executable = Path.Combine(output, "CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture");
            File.SetUnixFileMode(
                executable,
                File.GetUnixFileMode(executable)
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherExecute);

            var execution = await RunAsync(executable, string.Empty, TimeSpan.FromMinutes(1));
            execution.ExitCode.Should().Be(0, execution.Output);
            execution.Output.Should().Contain("ReflectionFallback_IsDisabled:PASS");
            execution.Output.Should().Contain("SerializeDeserialize_RepresentativeToolRoots:PASS");
            execution.Output.Should().Contain("CONTROL_PLANE_LOCALIZED_MESSAGE_NATIVEAOT_OK");
            execution.Output.Should().Contain("CONTROL_PLANE_JSON_CONTRACT_NATIVEAOT_OK");
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); } catch { }
        }
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
