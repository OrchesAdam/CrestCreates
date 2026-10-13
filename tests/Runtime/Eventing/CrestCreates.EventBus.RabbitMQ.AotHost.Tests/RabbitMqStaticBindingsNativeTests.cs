using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.AotHost.Tests;

/// <summary>
/// Native/trim publish-link-run gates for the static-binding RabbitMQ mainline. Real
/// `dotnet publish` for linux-x64 in trim mode and in NativeAOT mode (CrestCreatesPublishMode=aot),
/// executed against a real broker vhost prepared by this harness with explicit readiness, bounded
/// timeouts and process recycling. A missing broker fails the gate explicitly; there is no silent
/// skip. The gates do not extend any support statement to providers or hosts outside this fixture.
/// </summary>
public sealed class RabbitMqStaticBindingsNativeTests
{
    private const string Sentinel = "CRESTCREATES_RABBITMQ_STATIC_BINDINGS_NATIVE_OK";

    private static readonly string HostName = Environment.GetEnvironmentVariable("CREST_RABBITMQ_HOST") ?? "localhost";
    private static readonly int AmqpPort = int.TryParse(Environment.GetEnvironmentVariable("CREST_RABBITMQ_PORT"), out var port) ? port : 5672;
    private static readonly int ManagementPort = int.TryParse(Environment.GetEnvironmentVariable("CREST_RABBITMQ_MGMT_PORT"), out var mgmtPort) ? mgmtPort : 15672;

    [Fact]
    public async Task Publish_trimmed_fixture_runs_the_official_mainline_against_a_real_broker()
        => await RunGateAsync("trim", "CrestCreatesPublishMode=trim", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(4));

    [Fact]
    public async Task Publish_native_aot_fixture_runs_the_official_mainline_against_a_real_broker()
    {
        if (!OperatingSystem.IsLinux()
            || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                != System.Runtime.InteropServices.Architecture.X64)
            throw Xunit.Sdk.SkipException.ForSkip("The RabbitMQ NativeAOT gate is pinned to linux-x64.");

        await RunGateAsync("native-aot", "CrestCreatesPublishMode=aot", TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(6));
    }

    private static async Task RunGateAsync(string gate, string publishMode, TimeSpan publishTimeout, TimeSpan runTimeout)
    {
        await EnsureBrokerAsync();
        var vhost = "itest127aot_" + Guid.NewGuid().ToString("N")[..12];
        await CreateVHostAsync(vhost);

        var root = FindRepoRoot();
        var output = Path.Combine(Path.GetTempPath(), $"crest-127-aot-{gate}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);

        var evidence = new Dictionary<string, object?>
        {
            ["gate"] = "rabbitmq-static-bindings-" + gate,
            ["mode"] = publishMode,
            ["sha"] = ResolveSha(root),
            ["rid"] = "linux-x64",
            ["sdk"] = Environment.Version.ToString(),
            ["vhost"] = vhost
        };
        string? failure = null;

        try
        {
            var project = Path.Combine(
                root,
                "tests/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ.AotHost/CrestCreates.EventBus.RabbitMQ.AotHost.csproj");
            var publishCommand =
                $"publish \"{project}\" -c Release -r linux-x64 --self-contained true -p:{publishMode} " +
                $"--disable-build-servers -o \"{output}\"";
            evidence["publishCommand"] = "dotnet " + publishCommand;
            var publish = await RunProcessAsync("dotnet", publishCommand, publishTimeout);
            evidence["publishExitCode"] = publish.ExitCode;
            evidence["publishLog"] = publish.Output;
            publish.ExitCode.Should().Be(0, publish.Output);
            publish.Output.Should().Contain("Generating native code");
            publish.Output.Should().NotContain("warning IL2026");
            publish.Output.Should().NotContain("warning IL3050");

            var executable = Path.Combine(output, "CrestCreates.EventBus.RabbitMQ.AotHost");
            File.SetUnixFileMode(
                executable,
                File.GetUnixFileMode(executable)
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherExecute);

            var execution = await RunProcessAsync(
                executable,
                string.Empty,
                runTimeout,
                new Dictionary<string, string>
                {
                    ["CREST127_HOST"] = HostName,
                    ["CREST127_PORT"] = AmqpPort.ToString(),
                    ["CREST127_MGMT_PORT"] = ManagementPort.ToString(),
                    ["CREST127_VHOST"] = vhost,
                });
            evidence["executionExitCode"] = execution.ExitCode;
            evidence["executionOutput"] = execution.Output;
            execution.ExitCode.Should().Be(0, execution.Output);
            execution.Output.Should().Contain(Sentinel);
            execution.Output.Should().Contain("typed-cross-assembly:PASS");
            execution.Output.Should().Contain("refusals-to-dead-letter:PASS");
            execution.Output.Should().Contain("legacy-alias:PASS");
            execution.Output.Should().Contain("retry-then-success:PASS");
            execution.Output.Should().Contain("budget-exhaustion-to-dlq:PASS");
            execution.Output.Should().Contain("unroutable-publish-kind:PASS");
        }
        catch (Exception exception)
        {
            failure = $"{exception.GetType().Name}: {exception.Message}";
            throw;
        }
        finally
        {
            WriteEvidence(root, evidence, failure is null ? "passed" : "failed", failure);
            await DeleteVHostAsync(vhost);
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    private static async Task EnsureBrokerAsync()
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await tcp.ConnectAsync(HostName, AmqpPort, cts.Token);
        }
        catch (Exception ex)
        {
            Assert.Fail(
                $"RabbitMQ is not reachable at {HostName}:{AmqpPort} ({ex.Message}). " +
                "Start it with: DOCKER_HOST=unix:///run/user/1000/podman/podman.sock podman start crestcreates-test-rabbitmq");
        }
    }

    private static HttpClient CreateManagementClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{HostName}:{ManagementPort}/api/"),
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes("guest:guest")));
        return client;
    }

    private static async Task CreateVHostAsync(string vhost)
    {
        using var client = CreateManagementClient();
        using var created = await client.PutAsync(
            $"vhosts/{vhost}",
            new StringContent("""{"description":"itest127-aot","tags":[]}""", Encoding.UTF8, "application/json"));
        created.IsSuccessStatusCode.Should().BeTrue($"the harness must be able to create vhost '{vhost}'");

        using var permissions = await client.PutAsync(
            $"permissions/{vhost}/guest",
            new StringContent("""{"configure":".*","write":".*","read":".*"}""", Encoding.UTF8, "application/json"));
        permissions.IsSuccessStatusCode.Should().BeTrue($"the harness must be able to grant permissions on '{vhost}'");
    }

    private static async Task DeleteVHostAsync(string vhost)
    {
        try
        {
            using var client = CreateManagementClient();
            using var response = await client.DeleteAsync($"vhosts/{vhost}");
        }
        catch (Exception)
        {
        }
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        Dictionary<string, string>? environment = null)
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
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                process.StartInfo.Environment[pair.Key] = pair.Value;
            }
        }

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
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
            var name = fields["gate"]!.ToString() + ".json";
            File.WriteAllText(
                Path.Combine(directory, name!),
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
        {
            return sha!;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = root
            });
            if (process is null)
            {
                return "unknown";
            }

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
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
