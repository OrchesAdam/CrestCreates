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
/// #128 S2 baselines at real-MSBuild level. Every case writes a fresh random probe directory, runs
/// <c>dotnet build</c> to completion and only then reads the artifacts of that same build. The claims
/// are booked apart: the harness/closure control, the producer naming contract, the end of the
/// same-round mapping dependency, the closed CRUD root matrix in one build, and the fail-closed
/// counterfactual for a formal declaration whose toolchain is not assembled.
/// </summary>
public class CrudJsonContractMsBuildBaselineTests
{
    private const string ToolchainImports = """
        <Import Project="__ROOT__/src/Tooling/CrestCreates.JsonContracts.BuildTasks/build/CrestCreates.JsonContracts.Build.Repository.props" />
        <Import Project="__ROOT__/src/Tooling/CrestCreates.JsonContracts.BuildTasks/build/CrestCreates.JsonContracts.Build.Repository.targets" />
        """;

    private const string ProjectFile = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <OutputType>Library</OutputType>
            <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
            <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="__ROOT__/src/Tooling/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
            <ProjectReference Include="__ROOT__/src/Framework/Ddd/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Ddd/CrestCreates.Domain/CrestCreates.Domain.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Ddd/CrestCreates.Application.Contracts/CrestCreates.Application.Contracts.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Infrastructure/CrestCreates.Authorization.Abstractions/CrestCreates.Authorization.Abstractions.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Infrastructure/CrestCreates.DataFilter/CrestCreates.DataFilter.csproj" />
            <ProjectReference Include="__ROOT__/src/Core/CrestCreates.Core.Abstractions/CrestCreates.Core.Abstractions.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Infrastructure/CrestCreates.Aop/CrestCreates.Aop.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Infrastructure/CrestCreates.Validation/CrestCreates.Validation.csproj" />
            <ProjectReference Include="__ROOT__/src/Framework/Api/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj" />
          </ItemGroup>
        __IMPORTS__
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

    /// <summary>
    /// The §7.1 matrix that must be registered by the JsonContracts stage and then closed by the
    /// official STJ generator inside the same CoreCompile.
    /// </summary>
    private static readonly string[] RequiredCrudRoots =
    [
        "global::Shop.Application.Products.Contracts.ProductDto",
        "global::Shop.Application.Products.Contracts.CreateProductDto",
        "global::Shop.Application.Products.Contracts.UpdateProductDto",
        "global::Shop.Application.Products.Contracts.ProductListRequestDto",
        "global::CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto<global::Shop.Application.Products.Contracts.ProductDto>",
        "global::CrestCreates.DynamicApi.DynamicApiResponse<global::Shop.Application.Products.Contracts.ProductDto>",
        "global::CrestCreates.DynamicApi.DynamicApiResponse<global::CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto<global::Shop.Application.Products.Contracts.ProductDto>>",
        "global::CrestCreates.DynamicApi.DynamicApiResponse",
        "global::CrestCreates.Generated.ProductAppService_Update_CompatibilityInput",
        "global::CrestCreates.Generated.ProductAppService_Delete_CompatibilityInput",
    ];

    [Fact]
    public async Task Clean_probe_directory_exposes_the_same_build_producer_output_and_a_sound_closure()
    {
        var probe = await RunProbeAsync(withToolchain: true);

        probe.ProducerFiles.Should().NotBeEmpty(
            "the CRUD producer must materialize output inside the build that is being inspected");
        DiagnosticsIn(probe.Output, "Declaration.cs").Should().BeEmpty(
            "the authored formal declaration and the reference closure must compile; observed:\n" + probe.Output);

        ErrorLines(probe.Output).Should().OnlyContain(
            line => IsGeneratedFileDiagnostic(line),
            "no build error may come from authored input or an unresolved reference; observed: "
            + string.Join(" | ", ErrorLines(probe.Output)));
    }

    [Fact]
    public async Task Real_msbuild_producer_output_must_match_formal_naming_contract()
    {
        // §5.1: DTOs are named after typeof(Product) and live in the Contracts namespace below the
        // declaring service namespace. The materialized surface is read as ordinary Compile output.
        var probe = await RunProbeAsync(withToolchain: true);

        var bundle = probe.BundleSource;
        bundle.Should().Contain("class ProductDto",
            "DTOs are derived from typeof(Product), never from the annotated service class");
        bundle.Should().Contain("namespace Shop.Application.Products.Contracts",
            "the default DTO namespace is the declaring service namespace plus Contracts");
        bundle.Should().NotContain("class ProductAppServiceDto",
            "the annotated service class must never be treated as the entity");
    }

    [Fact]
    public async Task Current_msbuild_build_must_not_depend_on_same_round_mapping_output()
    {
        // The CRUD surface used to call mapper members produced by another generator in the same
        // round, so a consumer project could not compile. The materialized bundle plus the sibling
        // generators inside one CoreCompile must build.
        var probe = await RunProbeAsync(withToolchain: true);

        probe.ExitCode.Should().Be(0,
            "the generated CRUD surface must compile against the mapping output of the same build; observed:\n"
            + probe.Output);
    }

    [Fact]
    public async Task Formal_msbuild_build_must_close_crud_json_roots_in_the_same_build()
    {
        var probe = await RunProbeAsync(withToolchain: true);

        probe.ExitCode.Should().Be(0, "one build must close the CRUD JSON roots; observed:\n" + probe.Output);
        probe.Output.Should().NotContain("SYSLIB1030",
            "STJ must not report missing metadata for the materialized CRUD surface");

        var jsonContractsSource = probe.JsonContractsSource;
        jsonContractsSource.Should().NotBeEmpty("the JsonContracts stage must produce a generated file");
        foreach (var required in RequiredCrudRoots)
        {
            jsonContractsSource.Should().Contain(
                $"[global::System.Text.Json.Serialization.JsonSerializable(typeof({required}))]",
                $"the root matrix must register '{required}' on the generated CRUD context");
        }

        jsonContractsSource.Should().Contain("CrestCreatesCrudJsonSerializerContext",
            "the CRUD roots must be registered on one generated context");
        jsonContractsSource.Should().Contain(
            "JsonSourceGenerationOptions(PropertyNamingPolicy = global::System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)",
            "HTTP CRUD uses the camelCase policy of §7.1");

        probe.StjFiles.Should().NotBeEmpty("the official STJ generator must contribute the metadata");
        probe.StjFiles.Should().Contain(
            content => content.Contains(
                "JsonTypeInfo<global::Shop.Application.Products.Contracts.ProductDto>", StringComparison.Ordinal),
            "the official generator must emit metadata for the formal ProductDto root");
        probe.StjFiles.Should().Contain(
            content => content.Contains(
                "JsonTypeInfo<global::CrestCreates.DynamicApi.DynamicApiResponse<global::CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto<global::Shop.Application.Products.Contracts.ProductDto>>>",
                StringComparison.Ordinal),
            "the typed paged result envelope must have real metadata, not only a declared root");

        var outputsManifest = probe.OutputsManifest;
        outputsManifest.Should().Contain("\"OwnerServiceFullName\":\"Shop.Application.Products.ProductAppService\"",
            "every artifact must carry its owner identity (§6.2 provenance)");
        outputsManifest.Should().Contain("\"ContentHash\":\"sha256:",
            "artifacts must carry a content hash for incremental and manifest verification");
    }

    [Fact]
    public async Task Formal_declaration_without_the_crud_toolchain_must_fail_and_never_fall_back()
    {
        // Counterfactual booked apart from the closure case: the pre-S2 world where the CRUD surface
        // silently disappeared must now be a deterministic build error, never a legacy fallback.
        var probe = await RunProbeAsync(withToolchain: false);

        probe.ExitCode.Should().NotBe(0,
            "a formal CRUD declaration without the precompile + JSON toolchain must fail the build");
        probe.Output.Should().Contain("CCCRUD002",
            "the failure must be the assembled-toolchain diagnostic, not an unrelated error");
        probe.BundleSource.Should().BeEmpty(
            "nothing may be materialized without the CRUD precompile toolchain");
        probe.Output.Should().NotContain("class ProductAppServiceDto",
            "the legacy service-as-entity bundle must not take ownership back");
    }

    private static readonly string IntermediateRelativePath =
        Path.Combine("obj", "Debug", "net10.0");

    private sealed record ProbeBuild(
        string Directory,
        int ExitCode,
        string Output,
        IReadOnlyList<string> ProducerFiles,
        IReadOnlyList<string> StjFiles,
        string BundleSource,
        string JsonContractsSource,
        string OutputsManifest);

    private static async Task<ProbeBuild> RunProbeAsync(bool withToolchain)
    {
        var repoRoot = FindRepoRoot();
        await EnsureToolchainAsync(repoRoot);

        var probeDirectory = Path.Combine(
            repoRoot, "tests", "artifacts", "issue128-s2-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(probeDirectory);
        try
        {
            var project = ProjectFile
                .Replace("__ROOT__", repoRoot.Replace('\\', '/'))
                .Replace("__IMPORTS__", withToolchain ? ToolchainImports.Replace("__ROOT__", repoRoot.Replace('\\', '/')) : string.Empty);
            await File.WriteAllTextAsync(Path.Combine(probeDirectory, "Probe.csproj"), project);
            await File.WriteAllTextAsync(Path.Combine(probeDirectory, "Declaration.cs"), DeclarationSource);

            var buildResult = await RunDotnetAsync(
                repoRoot,
                $"build \"{Path.Combine(probeDirectory, "Probe.csproj")}\" -p:NuGetAudit=false --nologo",
                TimeSpan.FromMinutes(12));

            // The probe directory is deleted once the build is inspected, so every artifact the
            // assertions talk about is read here, from this same build.
            return new ProbeBuild(
                probeDirectory,
                buildResult.ExitCode,
                buildResult.Output,
                ReadGenerated(probeDirectory, "CrestCreates.CodeGenerator"),
                ReadGenerated(probeDirectory, "System.Text.Json.SourceGeneration"),
                ReadOptional(Path.Combine(probeDirectory, IntermediateRelativePath, "CrestCreates.Crud.g.cs")),
                ReadOptional(Path.Combine(probeDirectory, IntermediateRelativePath, "CrestCreates.JsonContracts.g.cs")),
                ReadOptional(Path.Combine(probeDirectory, IntermediateRelativePath, "CrestCreates.Crud.outputs.json")));
        }
        finally
        {
            try
            {
                Directory.Delete(probeDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// The repository transport resolves the task and tool from their build outputs, so the probe must
    /// not depend on someone having built them earlier in the session.
    /// </summary>
    private static async Task EnsureToolchainAsync(string repoRoot)
    {
        var taskAssembly = Path.Combine(
            repoRoot, "src", "Tooling", "CrestCreates.JsonContracts.BuildTasks", "bin", "Debug", "net10.0",
            "CrestCreates.JsonContracts.BuildTasks.dll");
        if (File.Exists(taskAssembly))
        {
            return;
        }

        await RunDotnetAsync(
            repoRoot,
            "build \"src/Tooling/CrestCreates.JsonContracts.BuildTasks/CrestCreates.JsonContracts.BuildTasks.csproj\" -p:NuGetAudit=false --nologo -v q",
            TimeSpan.FromMinutes(12));
    }

    private static string ReadOptional(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : string.Empty;

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

    private static bool IsGeneratedFileDiagnostic(string line) =>
        line.Contains(".g.cs(", StringComparison.Ordinal) || line.Contains("source-generators", StringComparison.Ordinal);

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
