using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CrestCreates.TestServices;
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

    private sealed record Broker(string Host, int AmqpPort, int ManagementPort, string User, string Password)
    {
        public static Broker FromEnvironment() => new(Required("CREST_RABBITMQ_HOST"),
            ReadPort("CREST_RABBITMQ_PORT"), ReadPort("CREST_RABBITMQ_MGMT_PORT"),
            Required("CREST_RABBITMQ_USER"), Required("CREST_RABBITMQ_PASSWORD"));

        private static string Required(string variable) => ExternalTestServices.RequiredConnectionString(variable, "RabbitMQ");

        private static int ReadPort(string variable) => int.TryParse(Required(variable), out var port) && port is > 0 and <= 65535
            ? port
            : throw new InvalidOperationException($"{variable} must be a valid TCP port.");
    }

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
        var broker = Broker.FromEnvironment();
        await TestServiceControl.WaitTcpAsync(broker.Host, broker.AmqpPort, TimeSpan.FromSeconds(30));
        using var management = CreateManagementClient(broker);
        var vhost = $"itest127aot_{Guid.NewGuid():N}";
        var root = FindRepoRoot();
        var output = Path.Combine(Path.GetTempPath(), $"crest-127-aot-{gate}-" + Guid.NewGuid().ToString("N"));
        var deleteVHost = false;
        var evidence = new Dictionary<string, object?>
        {
            ["gate"] = "rabbitmq-static-bindings-" + gate,
            ["mode"] = publishMode,
            ["sha"] = await ResolveShaAsync(root),
            ["rid"] = "linux-x64",
            ["sdk"] = Environment.Version.ToString(),
            ["vhost"] = vhost
        };
        Exception? failure = null;

        try
        {
            Directory.CreateDirectory(output);
            // A failed request may still have created this run's unique vhost.
            deleteVHost = true;
            await CreateVHostAsync(management, vhost, broker.User);
            var project = Path.Combine(
                root,
                "tests/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ.AotHost/CrestCreates.EventBus.RabbitMQ.AotHost.csproj");
            string[] publishArguments = ["publish", project, "-c", "Release", "-r", "linux-x64",
                "--self-contained", "true", $"-p:{publishMode}", "--disable-build-servers", "-o", output];
            evidence["publishCommand"] = "dotnet " + string.Join(' ', publishArguments);
            var publish = await TestServiceControl.RunProcessAsync("dotnet", publishArguments, publishTimeout,
                new Dictionary<string, string?> { ["MSBUILDDISABLENODEREUSE"] = "1" });
            evidence["publishExitCode"] = publish.ExitCode;
            evidence["publishLog"] = publish.Output;
            publish.ExitCode.Should().Be(0, publish.Output);

            var expectsNativeLink = publishMode.EndsWith("=aot", StringComparison.Ordinal);
            if (expectsNativeLink)
            {
                publish.Output.Should().Contain("Generating native code");
            }
            else
            {
                // The trim gate must produce a trimmed (JIT) product: a native link here would mean
                // the mode did not take effect and this would be a second native gate.
                publish.Output.Should().NotContain("Generating native code");
            }

            publish.Output.Should().NotContain("warning IL2026");
            publish.Output.Should().NotContain("warning IL3050");

            var executable = Path.Combine(output, "CrestCreates.EventBus.RabbitMQ.AotHost");
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    executable,
                    File.GetUnixFileMode(executable)
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherExecute);
            }

            var execution = await TestServiceControl.RunProcessAsync(executable, [], runTimeout,
                new Dictionary<string, string?>
                {
                    ["CREST127_HOST"] = broker.Host,
                    ["CREST127_PORT"] = broker.AmqpPort.ToString(),
                    ["CREST127_MGMT_PORT"] = broker.ManagementPort.ToString(),
                    ["CREST127_USER"] = broker.User,
                    ["CREST127_PASSWORD"] = broker.Password,
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
            failure = exception;
        }

        try
        {
            await ExternalTestServices.CleanupAsync([
                () => deleteVHost ? DeleteVHostAsync(management, vhost) : Task.CompletedTask,
                () =>
                {
                    if (Directory.Exists(output))
                        Directory.Delete(output, recursive: true);
                    return Task.CompletedTask;
                }
            ], failure);
            WriteEvidence(root, evidence, "passed", null);
        }
        catch (Exception exception)
        {
            WriteEvidence(root, evidence, "failed", $"{exception.GetType().Name}: {exception.Message}");
            throw;
        }
    }

    private static HttpClient CreateManagementClient(Broker broker)
    {
        var client = new HttpClient
        {
            BaseAddress = new UriBuilder("http", broker.Host, broker.ManagementPort, "/api/").Uri,
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{broker.User}:{broker.Password}")));
        return client;
    }

    private static async Task CreateVHostAsync(HttpClient client, string vhost, string user)
    {
        using var vhostBody = new StringContent("""{"description":"itest127-aot","tags":[]}""", Encoding.UTF8, "application/json");
        using var created = await client.PutAsync($"vhosts/{vhost}", vhostBody);
        created.EnsureSuccessStatusCode();

        using var permissionBody = new StringContent("""{"configure":".*","write":".*","read":".*"}""", Encoding.UTF8, "application/json");
        using var permissions = await client.PutAsync($"permissions/{vhost}/{Uri.EscapeDataString(user)}", permissionBody);
        permissions.EnsureSuccessStatusCode();
    }

    private static async Task DeleteVHostAsync(HttpClient client, string vhost)
    {
        using var response = await client.DeleteAsync($"vhosts/{vhost}");
        if (response.StatusCode != HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
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

    private static async Task<string> ResolveShaAsync(string root)
    {
        var sha = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(sha))
            return sha;

        try
        {
            var (exitCode, output) = await TestServiceControl.RunProcessAsync(
                "git", ["rev-parse", "HEAD"], TimeSpan.FromSeconds(10), workingDirectory: root);
            return exitCode == 0 ? output.Trim() : "unknown";
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
