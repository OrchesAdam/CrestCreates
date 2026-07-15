using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.AotFixture.Tests;

public sealed class McpAotFixtureTests
{
    [Fact]
    public async Task Publish_native_aot_fixture_executes_typed_input_and_output_path()
    {
        var root = FindRepoRoot();
        var output = Path.Combine(Path.GetTempPath(), "crest-mcp-aot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var project = Path.Combine(root, "tests/Integrations/CrestCreates.Mcp.AotFixture/CrestCreates.Mcp.AotFixture.csproj");
            var publish = await RunAsync(
                "dotnet",
                $"publish \"{project}\" -c Release -r linux-x64 --self-contained true -p:CrestCreatesPublishMode=aot --disable-build-servers -o \"{output}\"");
            publish.ExitCode.Should().Be(0, publish.Output);
            publish.Output.Should().NotContain("warning IL2026");
            publish.Output.Should().NotContain("warning IL3050");

            var executable = Path.Combine(output, "CrestCreates.Mcp.AotFixture");
            var execution = await RunAsync(executable, string.Empty);
            execution.ExitCode.Should().Be(0, execution.Output);
            execution.Output.Should().Contain("MCP_NATIVEAOT_PIPELINE_OK");
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string fileName, string arguments)
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Process '{fileName} {arguments}' exceeded the five-minute fixture timeout.");
        }
        return (process.ExitCode, await stdout + await stderr);
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
}
