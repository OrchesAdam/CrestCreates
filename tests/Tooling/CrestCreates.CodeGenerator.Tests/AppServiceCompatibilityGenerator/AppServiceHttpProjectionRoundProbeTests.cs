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
            [CapabilityCompatibilityProjection(RoutePrefix = "api/probe-orders")]
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
    public async Task Generator_materialized_combination_envelope_is_not_visible_to_the_official_stj_generator()
    {
        var run = await ProbeRunFor(HandWrittenServiceWithTwoValueParameters);
        {
            var generated = ReadGeneratedFiles(run.Directory, GeneratorFolderFragment);
            var envelopeFile = generated.FirstOrDefault(content =>
                content.Contains("class OrderAppService_Search_CompatibilityInput", StringComparison.Ordinal));
            envelopeFile.Should().NotBeNull(
                "pre-condition: the compatibility projection producer must really materialize the combined " +
                "input envelope in this build, otherwise nothing about its visibility is being measured; " +
                "generated files: " + string.Join(" | ", generated.Select(FileName)));
            envelopeFile.Should().Contain("namespace CrestCreates.Generated",
                "the probe must reference the producer's own namespace, so a failed lookup cannot be a "
                + "namespace spelling mistake");

            // Attribution is by location and by diagnostic id: the envelope exists in this compilation
            // (it is a generator output), so a CS0234/CS0246 on the authored file would mean a naming
            // mistake, not same-round invisibility. Only the STJ generator's own SYSLIB1030 proves the
            // round boundary.
            DiagnosticsIn(run.Output, "Declaration.cs").Should().OnlyContain(
                line => line.Contains("SYSLIB1030", StringComparison.Ordinal),
                "the authored context must fail only through the official generator's missing-metadata "
                + "diagnostic; any compiler name-resolution error here would be a naming mistake. Diagnostics: "
                + string.Join(" | ", DiagnosticsIn(run.Output, "Declaration.cs")));

            DiagnosticsIn(run.Output, "Declaration.cs").Should().Contain(
                line => line.Contains("SYSLIB1030", StringComparison.Ordinal)
                        && line.Contains("OrderAppService_Search_CompatibilityInput", StringComparison.Ordinal),
                "the official generator must report the envelope root it cannot close, by name");

            DiagnosticsIn(run.Output, "Declaration.cs").Should().NotContain(
                line => (line.Contains("CS0234") || line.Contains("CS0246"))
                        && line.Contains("OrderAppService_Search_CompatibilityInput", StringComparison.Ordinal),
                "the envelope must resolve as a compilation symbol; only the same-round generator must not see it");

            var stj = ReadGeneratedFiles(run.Directory, StjFolderFragment);
            stj.Should().Contain(
                content => content.Contains(
                    "JsonTypeInfo<global::ProbeOrdinary.OrdinaryEnvelope>", StringComparison.Ordinal),
                "the official generator must close the ordinary-Compile root inside this very build, otherwise "
                + "the absence of metadata for the envelope would only prove the generator never ran");
            stj.Should().NotContain(
                content => content.Contains("OrderAppService_Search_CompatibilityInput", StringComparison.Ordinal),
                "the official generator cannot produce metadata for an envelope another generator emits in the "
                + "same round");
        }
    }

    [Fact]
    public async Task Ordinary_compile_root_closes_in_the_same_build_as_the_generator_only_envelope()
    {
        // Same build, opposite half of the contract: the ordinary-Compile root in this very compilation
        // is closed by the official generator, so the envelope's absence is the round, not a dead driver.
        var run = await ProbeRunFor(HandWrittenServiceWithTwoValueParameters);
        {
            DiagnosticsIn(run.Output, "Declaration.cs").Should().NotContain(
                line => line.Contains("SYSLIB1030", StringComparison.Ordinal),
                "the control must show that an ordinary-Compile envelope of the same shape closes without a "
                + "missing-metadata diagnostic; diagnostics: "
                + string.Join(" | ", DiagnosticsIn(run.Output, "Declaration.cs")));

            var stj = ReadGeneratedFiles(run.Directory, StjFolderFragment);
            stj.Should().Contain(
                content => content.Contains(
                    "JsonTypeInfo<global::ProbeOrdinary.OrderSearchEnvelope>", StringComparison.Ordinal),
                "the official STJ generator ran in this build and closed the ordinary-Compile envelope; "
                + "otherwise the negative case above would prove nothing. STJ files: " + stj.Length);
        }
    }

    [Fact]
    public async Task Projection_activation_is_process_global_and_has_no_host_scoped_seam()
    {
        // Issue #129 R3: the cross-Host activation control. Same real build as the envelope probes, but
        // the claim is read off the producer's own syntax tree, not off file contents or comments.
        var run = await ProbeRunFor(HandWrittenServiceWithTwoValueParameters);
        {
            var trees = ReadGeneratedFiles(run.Directory, GeneratorFolderFragment)
                .Where(content => FileName(content).StartsWith("GeneratedAppServiceCompatibility", StringComparison.Ordinal))
                .Select(content => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                    content[..content.LastIndexOf("// FILE:", StringComparison.Ordinal)]))
                .ToList();

            trees.Should().NotBeEmpty("the probe project declares the projected service");

            var initializerMethods = trees
                .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>())
                .Where(method => method.AttributeLists
                    .SelectMany(list => list.Attributes)
                    .Any(attribute => attribute.Name.ToString().Contains("ModuleInitializer", StringComparison.Ordinal)))
                .ToList();

            initializerMethods.Should().NotBeEmpty(
                "the compatibility projection must be reachable without any host call, and it achieves that "
                + "by registering from a module initializer");

            var registrations = initializerMethods
                .SelectMany(method => method.DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
                .Select(invocation => invocation.Expression.ToString())
                .Where(expression => expression.Contains(".Register", StringComparison.Ordinal))
                .Select(expression =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        expression, @"(?<owner>[A-Za-z0-9_]+)\.(?<member>Register[A-Za-z0-9_]*)");
                    return match.Success ? match.Groups["owner"].Value + "." + match.Groups["member"].Value : expression;
                })
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            registrations.Should().Contain(new[]
            {
                "DescriptorProviderRegistry.Register",
                "CapabilityEndpointBindingRegistry.Register",
                "CapabilityHandlerResolverProvider.Register",
            }, "these are the process-global registries the projection writes into at assembly load; "
                + "observed: " + string.Join(", ", registrations));
            registrations.Should().HaveCountGreaterThanOrEqualTo(4,
                "descriptor, binding, invoker and manifest/result registration are all global today; observed: "
                + string.Join(", ", registrations));
            registrations.Should().ContainSingle(name => name == "AppServiceCompatibilityProjectionManifestRegistry.Register",
                "the projection manifest is also registered process-globally; observed: "
                + string.Join(", ", registrations));

            var wholeBuild = string.Join("\n", ReadGeneratedFiles(run.Directory, GeneratorFolderFragment));
            wholeBuild.Should().NotContain("ICapabilityEndpointContribution",
                "there is no per-Host contribution seam for the hand-written projection, so a host cannot "
                + "decline activation; this is the gap #129 closes with AddGeneratedAppServiceHttp()");
            wholeBuild.Should().NotContain("AddCrestCapabilityEndpointContribution",
                "the explicit-host composition used by the #128 CRUD path must not appear here");
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
                               || line.Contains(": warning ", StringComparison.Ordinal)))
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
