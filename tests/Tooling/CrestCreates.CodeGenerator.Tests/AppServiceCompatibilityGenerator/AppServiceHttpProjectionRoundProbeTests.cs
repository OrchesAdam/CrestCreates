using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;

/// <summary>
/// Issue #129 S1 supplemental contract probe (PR #152 R1-HTTP129-02). It runs a real
/// <c>dotnet build</c> with the real analyzer and the official System.Text.Json source generator, and
/// proves the mechanism behind the design claim: a combined input envelope materialized by a source
/// generator is not visible to the official STJ generator in the same CoreCompile, while a structurally
/// identical envelope declared as ordinary Compile input is. Nothing here depends on a not-yet-existing
/// API, so the red cause cannot be "an assertion about the future".
/// </summary>
[Collection("CrudBuild")]
public sealed class AppServiceHttpProjectionRoundProbeTests
{
    private const string GeneratorFolderFragment = "CrestCreates.CodeGenerator";
    private const string StjFolderFragment = "System.Text.Json.SourceGeneration";

    private const string HandWrittenServiceWithTwoValueParameters = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using CrestCreates.Domain.Shared.Attributes;

        namespace ProbeHandwritten
        {
            [CrestService]
            [CapabilityCompatibilityProjection(
                RoutePrefix = "api/probe-orders",
                RegistrationMode = HttpProjectionRegistrationMode.LegacyStatic)]
            public class OrderAppService
            {
                public Task<string> SearchAsync(string keyword, int take, CancellationToken cancellationToken)
                    => Task.FromResult(keyword + take);
            }
        }

        namespace ProbeOrdinary
        {
            public sealed class OrdinaryEnvelope
            {
                public string Keyword { get; init; } = string.Empty;
            }
        }

        namespace ProbeJson
        {
            // Two roots in one context: the ordinary-Compile type proves the official generator really ran
            // in this build, so the absence of metadata for the generator-only envelope is a round effect.
            [System.Text.Json.Serialization.JsonSerializable(typeof(ProbeOrdinary.OrdinaryEnvelope))]
            [System.Text.Json.Serialization.JsonSerializable(
                typeof(CrestCreates.Generated.OrderAppService_Search_CompatibilityInput))]
            public partial class ProbeEnvelopeContext : System.Text.Json.Serialization.JsonSerializerContext
            {
            }
        }
        """;

    private sealed record ProbeRun(int ExitCode, string Output, string Directory);

    // xUnit constructs a class instance per test, so a lifetime hook would rebuild for every test.
    // The projection is built once per source text and shared by all three controls.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ProbeRun> Runs = new();
    private static readonly SemaphoreSlim BuildGate = new(1, 1);

    private static async Task<ProbeRun> ProbeRunFor(string source)
    {
        await BuildGate.WaitAsync();
        try
        {
            if (!Runs.TryGetValue(source, out var cached))
            {
                cached = await RunProbeAsync(source);
                Runs[source] = cached;
            }

            return cached;
        }
        finally
        {
            BuildGate.Release();
        }
    }

    [Fact]
    public async Task Ordinary_compile_root_closes_in_the_same_build_as_the_generator_only_envelope()
    {
        // Same build, opposite half of the contract: the ordinary-Compile root in this very compilation
        // is closed by the official generator, so the envelope's absence is the round, not a dead driver.
        var run = await ProbeRunFor(HandWrittenServiceWithTwoValueParameters);
        {
            // The same context reports SYSLIB1030 for the generator-only envelope, so the control must be
            // about the ordinary root itself: no diagnostic may name it.
            DiagnosticsIn(run.Output, "Declaration.cs").Should().NotContain(
                line => line.Contains("OrdinaryEnvelope", StringComparison.Ordinal),
                "the ordinary-Compile root must not be the subject of any diagnostic on the authored file; "
                + "diagnostics: " + string.Join(" | ", DiagnosticsIn(run.Output, "Declaration.cs")));

            var stj = ReadGeneratedFiles(run.Directory, StjFolderFragment);
            stj.Should().Contain(
                content => content.Contains(
                    "JsonTypeInfo<global::ProbeOrdinary.OrdinaryEnvelope>", StringComparison.Ordinal),
                "the ordinary-Compile root of the same shape closes inside this very build, so the envelope's "
                + "missing metadata is the round boundary and not a dead generator. STJ files: " + stj.Length);
        }
    }

    private static async Task<ProbeRun> RunProbeAsync(string declarationSource)
    {
        var repoRoot = FindRepoRoot();
        var probeDirectory = Path.Combine(
            repoRoot, "tests", "artifacts", "issue129-s1-envelope-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(probeDirectory);

        var project = CrudJsonContractMsBuildBaselineTests.ProjectFile
            .Replace("__ROOT__", repoRoot.Replace('\\', '/'))
            .Replace("__IMPORTS__", string.Empty);

        await File.WriteAllTextAsync(Path.Combine(probeDirectory, "Probe.csproj"), project);
        await File.WriteAllTextAsync(Path.Combine(probeDirectory, "Declaration.cs"), declarationSource);

        var (exitCode, output) = await RunDotnetAsync(
            repoRoot,
            $"build \"{Path.Combine(probeDirectory, "Probe.csproj")}\" -p:NuGetAudit=false --nologo",
            TimeSpan.FromMinutes(20));

        return new ProbeRun(exitCode, output, probeDirectory);
    }

    private static string[] ReadGeneratedFiles(string probeDirectory, string generatorFolderFragment)
    {
        var root = Path.Combine(probeDirectory, "obj", "Debug", "net10.0", "source-generators");
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains(generatorFolderFragment, StringComparison.OrdinalIgnoreCase))
            .Select(path => File.ReadAllText(path) + "\n// FILE:" + Path.GetFileName(path))
            .ToArray();
    }

    private static string FileName(string content)
    {
        var index = content.LastIndexOf("// FILE:", StringComparison.Ordinal);
        return index < 0 ? "<unknown>" : content[(index + 8)..].Trim();
    }

    private static string[] DiagnosticsIn(string output, string fileName)
        => OutputLines(output)
            .Where(line => line.Contains(fileName, StringComparison.Ordinal)
                           && (line.Contains(": error ", StringComparison.Ordinal)
                               || line.Contains("SYSLIB", StringComparison.Ordinal)))
            .ToArray();

    private static string[] ErrorLines(string output)
        => OutputLines(output).Where(line => line.Contains(": error ", StringComparison.Ordinal)).Take(12).ToArray();

    private static string[] OutputLines(string output)
        => output.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(
        string workingDirectory,
        string arguments,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

        using var process = Process.Start(startInfo)!;
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => output.AppendLine(e.Data ?? string.Empty);
        process.ErrorDataReceived += (_, e) => output.AppendLine(e.Data ?? string.Empty);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutToken = new System.Threading.CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutToken.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {arguments} exceeded {timeout}");
        }

        return (process.ExitCode, output.ToString());
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryException("global.json not found above the test output directory");
    }
}

file sealed class DirectoryException : Exception
{
    public DirectoryException(string message) : base(message)
    {
    }
}
