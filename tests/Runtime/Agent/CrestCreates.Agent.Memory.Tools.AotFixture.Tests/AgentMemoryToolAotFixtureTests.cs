using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tools.AotFixture.Tests;

public sealed class AgentMemoryToolAotFixtureTests
{
    [Fact]
    public async Task Publish_native_aot_memory_tool_fixture_executes_generated_projection()
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            throw Xunit.Sdk.SkipException.ForSkip("The Memory Tool NativeAOT gate is pinned to linux-x64.");
        var root = FindRepoRoot();
        var output = Path.Combine(Path.GetTempPath(), "crest-agent-memory-aot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var project = Path.Combine(root, "tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.AotFixture/CrestCreates.Agent.Memory.Tools.AotFixture.csproj");
            var publish = await RunAsync("dotnet", $"publish \"{project}\" -c Release -r linux-x64 --self-contained true -p:CrestCreatesPublishMode=aot --disable-build-servers -o \"{output}\"");
            publish.ExitCode.Should().Be(0, publish.Output);
            publish.Output.Should().NotContain("warning IL2026");
            publish.Output.Should().NotContain("warning IL3050");
            var executable = Path.Combine(output, "CrestCreates.Agent.Memory.Tools.AotFixture");
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            var execution = await RunAsync(executable, string.Empty);
            execution.ExitCode.Should().Be(0, execution.Output);
            execution.Output.Should().Contain("agent_memory_build_pack: OK");
            execution.Output.Should().Contain("agent_memory_expand_source: OK");
            execution.Output.Should().Contain("agent_memory_curation_replay: OK");
            execution.Output.Should().Contain("agent_memory_accountability: OK");
            execution.Output.Should().Contain("AGENT_MEMORY_TOOL_NATIVEAOT_PIPELINE_OK");
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string fileName, string arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo { FileName = fileName, Arguments = arguments, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("NativeAOT fixture exceeded five minutes."); }
        return (process.ExitCode, await stdout + await stderr);
    }
    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null) { if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))) return current.FullName; current = current.Parent; }
        throw new InvalidOperationException("Repository root not found.");
    }
}
