using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.AotFixture.Tests;

public sealed class McpAotFixtureTests
{
    [Fact]
    public async Task Publish_native_aot_fixture_executes_typed_input_and_output_path()
    {
        if (!OperatingSystem.IsLinux()
            || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                != System.Runtime.InteropServices.Architecture.X64)
            throw Xunit.Sdk.SkipException.ForSkip("The Phase 8e NativeAOT gate is pinned to linux-x64.");

        var root = FindRepoRoot();
        var output = Path.Combine(Path.GetTempPath(), "crest-mcp-aot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var evidence = new Dictionary<string, object?>
        {
            ["gate"] = "mcp-native-pipeline",
            ["sha"] = ResolveSha(root),
            ["rid"] = "linux-x64"
        };
        string? failure = null;
        try
        {
            var project = Path.Combine(root, "tests/Integrations/CrestCreates.Mcp.AotFixture/CrestCreates.Mcp.AotFixture.csproj");
            var publishCommand = $"publish \"{project}\" -c Release -r linux-x64 --self-contained true -p:CrestCreatesPublishMode=aot --disable-build-servers -o \"{output}\"";
            evidence["publishCommand"] = "dotnet " + publishCommand;
            var publish = await RunAsync(
                "dotnet",
                publishCommand);
            evidence["publishExitCode"] = publish.ExitCode;
            evidence["publishLog"] = publish.Output;
            publish.ExitCode.Should().Be(0, publish.Output);
            publish.Output.Should().NotContain("warning IL2026");
            publish.Output.Should().NotContain("warning IL3050");

            var executable = Path.Combine(output, "CrestCreates.Mcp.AotFixture");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    executable,
                    File.GetUnixFileMode(executable)
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherExecute);
            }
            var execution = await RunAsync(executable, string.Empty);
            evidence["executionExitCode"] = execution.ExitCode;
            evidence["executionOutput"] = execution.Output;
            execution.ExitCode.Should().Be(0, execution.Output);
            execution.Output.Should().Contain("MCP_NATIVEAOT_PIPELINE_OK");
        }
        catch (Exception exception)
        {
            failure = $"{exception.GetType().Name}: {exception.Message}";
            throw;
        }
        finally
        {
            WriteEvidence(root, evidence, failure is null ? "passed" : "failed", failure);
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
        // Bounded budget sized for a cold CI publish (Release rebuild + IL link);
        // the whole gate must fail explicitly instead of waiting forever.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException($"Process '{fileName} {arguments}' exceeded the bounded fixture timeout.");
        }
        return (process.ExitCode, await stdout + await stderr);
    }

    private static void WriteEvidence(
        string root, Dictionary<string, object?> fields, string result, string? failure)
    {
        try
        {
            var directory = Path.Combine(root, "tests", "artifacts", "aot-native-evidence");
            Directory.CreateDirectory(directory);
            fields["result"] = result;
            fields["failure"] = failure;
            fields["writtenUtc"] = DateTime.UtcNow.ToString("o");
            File.WriteAllText(
                Path.Combine(directory, "mcp-native-pipeline.json"),
                JsonSerializer.Serialize(fields, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
        }
    }

    private static string ResolveSha(string root)
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
                WorkingDirectory = root
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
