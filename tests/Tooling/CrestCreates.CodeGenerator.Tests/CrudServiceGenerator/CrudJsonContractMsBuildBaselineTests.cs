using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

/// <summary>
/// #128 S1 baselines at real-MSBuild level. Every case creates its own random probe directory, writes
/// the sources, runs <c>dotnet build</c> to completion and only then reads the producer and STJ
/// artifacts of that same build — reading intermediate output before the build is not evidence.
/// The claims are booked separately: the harness/closure control, the same-round mapping gap, the
/// recorded STJ failure fact, the formal end-to-end contract (one build must close the roots) and
/// the producer naming gap.
/// </summary>
public class CrudJsonContractMsBuildBaselineTests
{
    private const string ProjectFile = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <OutputType>Library</OutputType>
            <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="__ROOT__/src/Tooling/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
            <ProjectReference Include="__ROOT__/src/Framework/Ddd/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Ddd/CrestCreates.Domain/CrestCreates.Domain.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Ddd/CrestCreates.Application.Contracts/CrestCreates.Application.Contracts.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Infrastructure/CrestCreates.Authorization.Abstractions/CrestCreates.Authorization.Abstractions.csproj" />
            <ProjectReference Include="__ROOT__/src/Core/CrestCreates.Core.Abstractions/CrestCreates.Core.Abstractions.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Infrastructure/CrestCreates.Aop/CrestCreates.Aop.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Infrastructure/CrestCreates.Validation/CrestCreates.Validation.csproj" />
          </ItemGroup>
        </Project>
        """;

    private const string DeclarationSource = """
        using System;
        using CrestCreates.Domain.Shared.Attributes;

        namespace Shop.Domain.Entities
        {
            public class Product
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = string.Empty;
                public decimal Price { get; set; }
            }
        }

        namespace Shop.Application.Products
        {
            [GenerateCrudService(typeof(Shop.Domain.Entities.Product), RoutePrefix = "/api/products")]
            public partial class ProductAppService
            {
            }
        }
        """;

    // Roots written with the names the producer emits today, so the recorded failure fact is
    // attributable to STJ metadata generation rather than to the naming gap.
    private const string CurrentNameJsonRootsSource = """
        using System.Text.Json.Serialization;
        using Shop.Application.Products.Dtos;

        namespace Shop.Serialization;

        [JsonSerializable(typeof(ProductAppServiceDto))]
        [JsonSerializable(typeof(CreateProductAppServiceDto))]
        [JsonSerializable(typeof(UpdateProductAppServiceDto))]
        internal partial class ProbeJsonContext : JsonSerializerContext
        {
        }
        """;

    private static readonly string[] CurrentNameRootTypes =
    [
        "ProductAppServiceDto",
        "CreateProductAppServiceDto",
        "UpdateProductAppServiceDto",
    ];

    [Fact]
    public async Task Clean_probe_directory_exposes_the_same_build_producer_output_and_a_sound_closure()
    {
        // Harness control (R4-CRUD128-01): fresh random directory, one real build, artifacts read
        // only after it completed. The authored declaration must contribute no diagnostic at all, so
        // every other case in this file is attributable to the generated chain, not to the probe or
        // to a missing reference.
        var probe = await RunProbeAsync(writeJsonRoots: null);

        probe.ProducerFiles.Should().NotBeEmpty(
            "the CRUD generator must materialize output inside the build that is being inspected");
        probe.ProducerOutput.Should().Contain("class ProductAppServiceDto",
            "the producer output read after the build must be this build's own CRUD bundle");

        DiagnosticsIn(probe.Output, "Declaration.cs").Should().BeEmpty(
            "the authored formal declaration and the reference closure must compile; observed:\n" + probe.Output);

        var blockingErrors = ErrorLines(probe.Output);
        blockingErrors.Should().OnlyContain(line => IsGeneratedBundleDiagnostic(line),
            "no build error may come from authored input or from an unresolved reference; observed: "
            + string.Join(" | ", blockingErrors));
    }

    [Fact]
    public async Task Current_msbuild_build_must_not_depend_on_same_round_mapping_output()
    {
        // Recorded gap, booked apart from the JSON roots: the CRUD surface the generator emits calls
        // mapping members (ProductAppServiceObjectMappings.ToTarget/Apply) and entity identity that
        // only exist if another producer output is visible in the same compilation. A consumer
        // project with nothing but the formal declaration must therefore build today, and does not.
        var probe = await RunProbeAsync(writeJsonRoots: null);

        DiagnosticsIn(probe.Output, "Declaration.cs").Should().BeEmpty(
            "the failure must not be attributed to the authored declaration; observed:\n" + probe.Output);

        probe.ExitCode.Should().Be(0,
            "the generated CRUD surface must not depend on same-round mapping output; observed:\n" + probe.Output);
    }

    [Fact]
    public async Task Current_msbuild_build_fails_because_stj_generates_no_metadata_for_the_same_round_dtos()
    {
        // Recorded failure fact: the DTOs exist only inside the generator round, so the official STJ
        // generator runs without their metadata. Attribution is per location: the authored roots file
        // must fail only with SYSLIB1030 naming each declared root, and the STJ output of this same
        // build must carry no JsonTypeInfo for those types. Unrelated errors (the legacy bundle) are
        // located elsewhere and cannot satisfy this case.
        var probe = await RunProbeAsync(CurrentNameJsonRootsSource);

        probe.ExitCode.Should().NotBe(0,
            "the build must fail while the CRUD surface depends on same-round generator output");

        var rootsDiagnostics = DiagnosticsIn(probe.Output, "JsonRoots.cs");
        rootsDiagnostics.Should().OnlyContain(
            line => line.Contains("SYSLIB1030", StringComparison.Ordinal),
            "the authored roots may only fail because STJ generated no metadata for them; observed: "
            + string.Join(" | ", rootsDiagnostics));

        foreach (var declaredRoot in CurrentNameRootTypes)
        {
            probe.ProducerOutput.Should().Contain("class " + declaredRoot,
                $"the producer must have generated '{declaredRoot}' in this same build");
            rootsDiagnostics.Should().Contain(
                line => line.Contains(declaredRoot, StringComparison.Ordinal),
                $"'{declaredRoot}' must be named by the SYSLIB1030 diagnostics on the authored roots");
        }

        probe.StjFiles.Should().NotBeEmpty(
            "the official STJ generator must have run in this build, otherwise metadata absence is not evidence");
        probe.StjFiles.Should().NotContain(
            content => content.Contains("JsonTypeInfo<global::Shop.Application.Products.Dtos.ProductAppServiceDto>"),
            "STJ must not generate metadata for same-round generator output; if that metadata exists "
            + "this case no longer proves same-round invisibility");
    }

    [Fact]
    public async Task Formal_msbuild_build_must_close_crud_json_roots_in_the_same_build()
    {
        // Designed end-to-end contract (§6.2/§7.1): the CRUD DTOs and mapping seeds are materialized
        // before ordinary compilation, so one build resolves the formal roots, the official STJ
        // generator closes them, and the bundle compiles. Today the same build fails, which is the
        // gap this baseline books for S2.
        var probe = await RunProbeAsync(FormalJsonRootsSource);

        probe.ExitCode.Should().Be(0,
            "one build must close the CRUD JSON roots and the generated surface; observed:\n" + probe.Output);

        DiagnosticsIn(probe.Output, "JsonRoots.cs").Should().BeEmpty(
            "the declared formal roots must resolve inside the same build");

        probe.StjFiles.Should().NotBeEmpty("the official STJ generator must contribute the metadata");
        probe.StjFiles.Should().Contain(
            content => content.Contains("JsonTypeInfo<global::Shop.Application.Products.Contracts.ProductDto>"),
            "the STJ output must carry metadata for the formal ProductDto root");
    }

    [Fact]
    public async Task Real_msbuild_producer_output_must_match_formal_naming_contract()
    {
        // Naming gap booked on its own (§5.1): DTOs are named after typeof(Product) and live in the
        // Contracts namespace below the declaring service namespace, never after the service class.
        var probe = await RunProbeAsync(writeJsonRoots: null);

        probe.ProducerFiles.Should().NotBeEmpty("the CRUD generator must produce output");
        probe.ProducerOutput.Should().Contain("class ProductDto",
            "DTOs must be named after typeof(Product), not the service class");
        probe.ProducerOutput.Should().Contain("namespace Shop.Application.Products.Contracts",
            "DTOs must be in the Contracts namespace per the formal contract");
        probe.ProducerOutput.Should().NotContain("class ProductAppServiceDto",
            "the service class must not be treated as the entity");
    }

    private const string FormalJsonRootsSource = """
        using System.Text.Json.Serialization;
        using Shop.Application.Products.Contracts;

        namespace Shop.Serialization;

        [JsonSerializable(typeof(ProductDto))]
        [JsonSerializable(typeof(CreateProductDto))]
        [JsonSerializable(typeof(UpdateProductDto))]
        [JsonSerializable(typeof(ProductListRequestDto))]
        internal partial class ProbeJsonContext : JsonSerializerContext
        {
        }
        """;

    private sealed record ProbeBuild(
        int ExitCode,
        string Output,
        IReadOnlyList<string> ProducerFiles,
        IReadOnlyList<string> StjFiles)
    {
        public string ProducerOutput { get; } = string.Join("\n", ProducerFiles);
    }

    private static async Task<ProbeBuild> RunProbeAsync(string? writeJsonRoots)
    {
        var repoRoot = FindRepoRoot();
        var probeDirectory = Path.Combine(
            repoRoot, "tests", "artifacts", "issue128-msbuild-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(probeDirectory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(probeDirectory, "Probe.csproj"),
                ProjectFile.Replace("__ROOT__", repoRoot.Replace('\\', '/')));
            await File.WriteAllTextAsync(Path.Combine(probeDirectory, "Declaration.cs"), DeclarationSource);
            if (writeJsonRoots is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(probeDirectory, "JsonRoots.cs"), writeJsonRoots);
            }

            var buildResult = await RunDotnetAsync(
                repoRoot,
                $"build \"{Path.Combine(probeDirectory, "Probe.csproj")}\" -p:NuGetAudit=false --nologo",
                TimeSpan.FromMinutes(12));

            // R4-CRUD128-01: the artifacts of this same build are read only after it completed.
            return new ProbeBuild(
                buildResult.ExitCode,
                buildResult.Output,
                ReadGenerated(probeDirectory, "CrestCreates.CodeGenerator"),
                ReadGenerated(probeDirectory, "System.Text.Json.SourceGeneration"));
        }
        finally
        {
            try
            {
                Directory.Delete(probeDirectory, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }

    private static IReadOnlyList<string> ReadGenerated(string probeDirectory, string generatorFolderFragment)
    {
        var intermediateRoot = Path.Combine(probeDirectory, "obj");
        if (!Directory.Exists(intermediateRoot))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(intermediateRoot, "*.g.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains(generatorFolderFragment, StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();
    }

    private static bool IsGeneratedBundleDiagnostic(string line) =>
        line.Contains(".g.cs(", StringComparison.Ordinal)
        || line.Contains("source-generators", StringComparison.Ordinal);

    private static IReadOnlyList<string> DiagnosticsIn(string output, string fileName) =>
        OutputLines(output)
            .Where(line => line.Contains(fileName + "(", StringComparison.Ordinal)
                           && (line.Contains(": error ", StringComparison.Ordinal)
                               || line.Contains(": warning ", StringComparison.Ordinal)))
            .ToArray();

    private static IReadOnlyList<string> ErrorLines(string output) =>
        OutputLines(output).Where(line => line.Contains(": error ", StringComparison.Ordinal)).ToArray();

    private static IEnumerable<string> OutputLines(string output) =>
        output.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);

    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(
        string workingDirectory, string arguments, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        process.StartInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new System.Threading.CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException($"dotnet {arguments} exceeded the bounded probe timeout.");
        }

        return (process.ExitCode, await stdout + await stderr);
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
