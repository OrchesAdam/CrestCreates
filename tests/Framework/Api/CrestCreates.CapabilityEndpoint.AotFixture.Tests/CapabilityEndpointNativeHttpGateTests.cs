using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace CrestCreates.CapabilityEndpoint.AotFixture.Tests;

/// <summary>
/// Executes the generated HTTP endpoint inside a real NativeAOT process:
/// Release publish(CrestCreatesPublishMode=aot) → native link → launch the
/// published executable on a loopback port → readiness → HTTP assertions →
/// process reclamation → evidence capture. The web-application-factory tests
/// in <see cref="AotFixtureTests"/> remain the JIT integration signal and are
/// not a substitute for this gate.
/// </summary>
[Trait("Category", "NativeAotGate")]
public sealed class CapabilityEndpointNativeHttpGateTests
{
    private const string Rid = "linux-x64";

    private static readonly Lazy<Task<PublishedFixture>> Published = new(PublishFixtureAsync);

    [Fact]
    public async Task Native_http_gate_serves_generated_endpoint_against_published_binary()
    {
        RequireLinuxX64();

        var outcome = await RunGateAsync(correctContentExpectation: true);

        outcome.PublishExitCode.Should().Be(0, outcome.PublishLog);
        // Scope the IL warning assertion to the fixture's own compilation: the
        // fixture project treats IL2026/IL3050 as errors. Pre-existing warnings
        // from dependency projects (Application.Contracts, Aop) stay visible in
        // the captured publish log but are not this gate's signal.
        FixtureTaggedIlWarnings(outcome.PublishLog).Should().BeEmpty(
            "the fixture project must not emit AOT/trim warnings for its own code");
        outcome.Passed.Should().BeTrue(outcome.Failure);
        outcome.ProcessReaped.Should().BeTrue(
            "the gate must recycle the native server process it started");
        File.Exists(outcome.EvidencePath).Should().BeTrue(
            "successful gate runs must persist evidence for CI upload");
    }

    [Fact]
    public async Task Native_http_gate_fails_when_content_expectation_is_wrong()
    {
        RequireLinuxX64();

        var outcome = await RunGateAsync(correctContentExpectation: false);

        outcome.Passed.Should().BeFalse(
            "a wrong HTTP content expectation must fail the gate instead of producing a green signal");
        outcome.Failure.Should().NotBeNull();
        outcome.Failure.Should().Contain("XunitException",
            "the wrong expectation must surface as an HTTP content assertion failure, not as a startup or timeout error");
        outcome.ProcessReaped.Should().BeTrue(
            "failed gate runs must still recycle the native server process");
        File.ReadAllText(outcome.EvidencePath).Should()
            .Contain("\"result\": \"failed\"")
            .And.Contain("\"expectationMode\": \"deliberately-wrong\"");
    }

    private static IEnumerable<string> FixtureTaggedIlWarnings(string publishLog)
        => publishLog
            .Split('\n')
            .Where(line =>
                (line.Contains("warning IL2026", StringComparison.Ordinal)
                 || line.Contains("warning IL3050", StringComparison.Ordinal))
                && line.Contains("CrestCreates.CapabilityEndpoint.AotFixture.csproj", StringComparison.Ordinal));

    private static async Task<GateRunOutcome> RunGateAsync(bool correctContentExpectation)
    {
        var fixture = await Published.Value;
        var evidence = new EvidenceCollector(
            correctContentExpectation
                ? "capability-endpoint-native-http"
                : "capability-endpoint-native-http-negative");
        var transcript = new List<Dictionary<string, object?>>();

        evidence.Add("gate", "capability-endpoint-native-http");
        evidence.Add("expectationMode", correctContentExpectation ? "correct" : "deliberately-wrong");
        evidence.Add("sha", NativeAotGateSupport.ResolveSha());
        evidence.Add("rid", Rid);
        evidence.Add("publishCommand", fixture.Command);
        evidence.Add("publishExitCode", fixture.PublishExitCode);
        evidence.Add("publishDurationSeconds", Math.Round(fixture.PublishDuration.TotalSeconds, 1));
        evidence.Add("publishLog", fixture.PublishLog);

        string? failure = null;
        var processReaped = false;
        NativeServerProcess? server = null;
        var baseUrl = string.Empty;

        try
        {
            if (fixture.PublishExitCode != 0)
                throw new InvalidOperationException("NativeAOT publish failed; see publishLog for details.");

            (server, baseUrl) = await StartWithBoundedBindRetryAsync(fixture);
            evidence.Add("processId", server.ProcessId);
            evidence.Add("baseUrl", baseUrl);

            using var http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(10)
            };

            await NativeGateWaiter.WaitForHttpReadyAsync(
                server, http, "/api/greeting/list-greetings", TimeSpan.FromSeconds(30));

            await RunHttpMatrixAsync(http, transcript, correctContentExpectation);
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            if (server is not null)
            {
                var exitCode = await server.ReapAsync();
                processReaped = server.HasExited;
                evidence.Add("processExitCodeAfterShutdown", exitCode);
                evidence.Add("stdout", server.StandardOutput);
                evidence.Add("stderr", server.StandardError);
                await server.DisposeAsync();
            }

            evidence.Add("requests", transcript);
            evidence.Write(failure is null ? "passed" : "failed", failure);
        }

        return new GateRunOutcome(
            Passed: failure is null,
            Failure: failure,
            ProcessReaped: processReaped,
            PublishExitCode: fixture.PublishExitCode,
            PublishLog: fixture.PublishLog,
            EvidencePath: evidence.FilePath);
    }

    private static async Task<(NativeServerProcess Server, string BaseUrl)> StartWithBoundedBindRetryAsync(
        PublishedFixture fixture)
    {
        const int maxAttempts = 3;
        string? lastOutput = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var port = NativeAotGateSupport.ReserveLoopbackPort();
            var baseUrl = $"http://127.0.0.1:{port}";
            var server = NativeServerProcess.Start(
                fixture.ExecutablePath, fixture.OutputDirectory, baseUrl);
            try
            {
                await NativeGateWaiter.WaitForListeningLineAsync(server, TimeSpan.FromSeconds(30));
                server.StandardOutput.Should().Contain(
                    $"Now listening on: {baseUrl}",
                    "the listening line must come from this child process on the port reserved for this run");
                return (server, baseUrl);
            }
            catch (NativeFixtureStartupException ex)
            {
                lastOutput = ex.StandardOutput + Environment.NewLine + ex.StandardError;
                var lostBindRace = lastOutput.Contains(
                    "address already in use", StringComparison.OrdinalIgnoreCase);
                await server.DisposeAsync();
                if (!lostBindRace || attempt == maxAttempts)
                    throw;
            }
        }

        throw new NativeFixtureStartupException(
            $"Native fixture failed to bind a loopback port after {maxAttempts} bounded attempts.",
            lastOutput ?? string.Empty,
            string.Empty);
    }

    private static async Task RunHttpMatrixAsync(
        HttpClient http,
        List<Dictionary<string, object?>> transcript,
        bool correctContentExpectation)
    {
        var uniqueName = "gate-" + Guid.NewGuid().ToString("N");

        var list = await SendAsync(transcript, http, "get-list", HttpMethod.Get,
            "/api/greeting/list-greetings", body: null);
        list.Status.Should().Be(200, $"GET list must succeed. Body: {list.Body}");
        using (var document = JsonDocument.Parse(list.Body))
        {
            document.RootElement.GetProperty("code").GetInt32().Should().Be(200);
            var data = document.RootElement.GetProperty("data");
            data.ValueKind.Should().Be(JsonValueKind.Array,
                "the official GET envelope must carry the collection under 'data'");
            data.GetArrayLength().Should().Be(0,
                "the official empty-list semantics must be preserved");
        }

        var post = await SendAsync(transcript, http, "post-valid", HttpMethod.Post,
            "/api/greeting/process-greeting", JsonSerializer.Serialize(new { Name = uniqueName }));
        post.Status.Should().Be(200, $"valid POST must succeed. Body: {post.Body}");
        using (var document = JsonDocument.Parse(post.Body))
        {
            document.RootElement.GetProperty("code").GetInt32().Should().Be(200);
            var message = document.RootElement.GetProperty("data").GetProperty("message").GetString();
            var expected = correctContentExpectation
                ? $"Hello, {uniqueName}!"
                : $"Hello, {uniqueName}-WRONG!";
            message.Should().Be(expected,
                "the response payload must round-trip the request-unique value through native binding, business execution and serialization");
        }

        var malformed = await SendAsync(transcript, http, "post-malformed", HttpMethod.Post,
            "/api/greeting/process-greeting", "{ this is not valid json");
        malformed.Status.Should().Be(400,
            "malformed JSON must follow the body reader contract and must never execute the business path");
        malformed.Body.Should().BeEmpty(
            "the current compatibility body contract returns an empty 400 body");

        var wrongType = await SendAsync(transcript, http, "post-incompatible-field-type", HttpMethod.Post,
            "/api/greeting/process-greeting", "{\"Name\":123}");
        wrongType.Status.Should().Be(400,
            "an incompatible field type must be rejected instead of executing the business path");

        var unmapped = await SendAsync(transcript, http, "get-unmapped-route", HttpMethod.Get,
            "/api/greeting/no-such-endpoint", body: null);
        unmapped.Status.Should().Be(404,
            "an unmapped route must surface as 404 instead of being swallowed by a fallback");
    }

    private static async Task<HttpCaseResult> SendAsync(
        List<Dictionary<string, object?>> transcript,
        HttpClient http,
        string caseName,
        HttpMethod method,
        string path,
        string? body)
    {
        var startedAt = DateTime.UtcNow;
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        var duration = DateTime.UtcNow - startedAt;

        transcript.Add(new Dictionary<string, object?>
        {
            ["case"] = caseName,
            ["method"] = method.Method,
            ["path"] = path,
            ["requestBody"] = body,
            ["status"] = (int)response.StatusCode,
            ["responseBody"] = responseBody,
            ["durationMs"] = Math.Round(duration.TotalMilliseconds, 1)
        });

        return new HttpCaseResult((int)response.StatusCode, responseBody);
    }

    private static async Task<PublishedFixture> PublishFixtureAsync()
    {
        var root = NativeAotGateSupport.FindRepoRoot();
        var outputDirectory = Path.Combine(
            Path.GetTempPath(), "crest-capability-http-aot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        var project = Path.Combine(
            root,
            "tests/Framework/Api/CrestCreates.CapabilityEndpoint.AotFixture/CrestCreates.CapabilityEndpoint.AotFixture.csproj");
        var command =
            $"publish \"{project}\" -c Release -r {Rid} --self-contained true " +
            $"-p:CrestCreatesPublishMode=aot --disable-build-servers -o \"{outputDirectory}\"";

        ProcessResult publish;
        try
        {
            publish = await NativeAotGateSupport.RunProcessAsync(
                "dotnet", command, TimeSpan.FromMinutes(20));
        }
        catch (TimeoutException)
        {
            TryDelete(outputDirectory);
            throw;
        }

        var executable = Path.Combine(outputDirectory, "CrestCreates.CapabilityEndpoint.AotFixture");
        if (publish.ExitCode == 0 && !OperatingSystem.IsWindows() && File.Exists(executable))
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
            PublishLog: publish.Output,
            PublishDuration: publish.Duration);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception)
        {
        }
    }

    private static void RequireLinuxX64()
    {
        if (!OperatingSystem.IsLinux()
            || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                != System.Runtime.InteropServices.Architecture.X64)
        {
            throw SkipException.ForSkip($"The native HTTP gate is pinned to {Rid}.");
        }
    }

    private sealed record PublishedFixture(
        string OutputDirectory,
        string ExecutablePath,
        string Command,
        int PublishExitCode,
        string PublishLog,
        TimeSpan PublishDuration);

    private sealed record GateRunOutcome(
        bool Passed,
        string? Failure,
        bool ProcessReaped,
        int PublishExitCode,
        string PublishLog,
        string EvidencePath);

    private sealed record HttpCaseResult(int Status, string Body);
}
