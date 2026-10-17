using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
            [CapabilityCompatibilityProjection(RoutePrefix = "api/probe-orders")]
            public class OrderAppService
            {
                public Task<string> SearchAsync(string keyword, int take, CancellationToken cancellationToken)
                    => Task.FromResult(keyword + take);
            }
        }

        namespace ProbeJson
        {
            [System.Text.Json.Serialization.JsonSerializable(
                typeof(ProbeHandwritten.OrderAppService_Search_CompatibilityInput))]
            public partial class ProbeEnvelopeContext : System.Text.Json.Serialization.JsonSerializerContext
            {
            }
        }
        """;

    /// <summary>
    /// Control: the same envelope shape written as ordinary Compile input, which the official
    /// generator must be able to close. Without this the red above could be "STJ never runs".
    /// </summary>
    private const string OrdinaryCompileEnvelopeControl = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using CrestCreates.Domain.Shared.Attributes;

        namespace ProbeOrdinary
        {
            public sealed class OrderSearchEnvelope
            {
                public string Keyword { get; init; } = string.Empty;
                public int Take { get; init; }
            }

            [CrestService]
            [CapabilityCompatibilityProjection(RoutePrefix = "api/probe-orders")]
            public class OrderAppService
            {
                public Task<string> SearchAsync(string keyword, int take, CancellationToken cancellationToken)
                    => Task.FromResult(keyword + take);
            }
        }

        namespace ProbeJson
        {
            [System.Text.Json.Serialization.JsonSerializable(typeof(ProbeOrdinary.OrderSearchEnvelope))]
            public partial class ProbeEnvelopeContext : System.Text.Json.Serialization.JsonSerializerContext
            {
            }
        }
        """;

    private sealed record ProbeRun(int ExitCode, string Output, string Directory);

    [Fact]
    public async Task Generator_materialized_combination_envelope_is_not_visible_to_the_official_stj_generator()
    {
        var run = await RunProbeAsync(HandWrittenServiceWithTwoValueParameters);
        try
        {
            var generated = ReadGeneratedFiles(run.Directory, GeneratorFolderFragment);
            var envelopeFile = generated.FirstOrDefault(content =>
                content.Contains("class OrderAppService_Search_CompatibilityInput", StringComparison.Ordinal));
            envelopeFile.Should().NotBeNull(
                "pre-condition: the compatibility projection producer must really materialize the combined " +
                "input envelope in this build, otherwise nothing about its visibility is being measured; " +
                "generated files: " + string.Join(" | ", generated.Select(FileName)));

            DiagnosticsIn(run.Output, "Declaration.cs").Should().Contain(
                line => (line.Contains("CS0246") || line.Contains("CS0234"))
                        && line.Contains("OrderAppService_Search_CompatibilityInput", StringComparison.Ordinal),
                "the context declaration in ordinary Compile input cannot resolve a type another generator emits "
                + "in the same round (observed as CS0234/CS0246 naming the envelope). This is the mechanism #128 "
                + "closed by pre-materializing artifacts; the hand-written projection has no equivalent yet. Diagnostics: "
                + string.Join(" | ", ErrorLines(run.Output)));

            var stj = ReadGeneratedFiles(run.Directory, StjFolderFragment);
            stj.Should().NotContain(
                content => content.Contains("OrderAppService_Search_CompatibilityInput", StringComparison.Ordinal),
                "the official generator cannot produce metadata for the generator-only envelope");
        }
        finally
        {
            Delete(run.Directory);
        }
    }

    [Fact]
    public async Task Ordinary_compile_envelope_of_the_same_shape_is_closed_by_the_official_stj_generator()
    {
        var run = await RunProbeAsync(OrdinaryCompileEnvelopeControl);
        try
        {
            DiagnosticsIn(run.Output, "Declaration.cs").Should().NotContain(
                line => (line.Contains("CS0246") || line.Contains("CS0234"))
                        && line.Contains("OrderSearchEnvelope", StringComparison.Ordinal),
                "the control must show that ordinary Compile input is visible; diagnostics: "
                + string.Join(" | ", ErrorLines(run.Output)));

            var stj = ReadGeneratedFiles(run.Directory, StjFolderFragment);
            stj.Should().Contain(
                content => content.Contains(
                    "JsonTypeInfo<global::ProbeOrdinary.OrderSearchEnvelope>", StringComparison.Ordinal),
                "the official STJ generator ran in this build and closed the ordinary-Compile envelope; "
                + "otherwise the negative case above would prove nothing. STJ files: " + stj.Length);
        }
        finally
        {
            Delete(run.Directory);
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
            .Where(line => line.Contains(fileName, StringComparison.Ordinal) && line.Contains(": error ", StringComparison.Ordinal))
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
