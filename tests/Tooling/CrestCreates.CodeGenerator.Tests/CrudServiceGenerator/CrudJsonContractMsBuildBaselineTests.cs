using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

/// <summary>
/// #128 S1 baseline at real-MSBuild level: a genuine consumer project declares the entity, the
/// formal <c>[GenerateCrudService(typeof(Entity))]</c> partial service and the official
/// System.Text.Json roots for the CRUD DTOs. The designed contract materializes the DTOs before
/// compilation so the roots resolve and the STJ source generator closes them in the same build.
/// Today the DTOs exist only inside the generator round, so the authored roots cannot resolve and
/// the build fails — the recorded same-round-invisibility gap, with only the DTO references as the
/// failure reason (the project closure below is otherwise complete and compiles).
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

    private const string JsonRootsSource = """
        using System.Text.Json.Serialization;
        using Shop.Application.Products.Contracts;

        namespace Shop.Serialization;

        [JsonSerializable(typeof(ProductDto))]
        [JsonSerializable(typeof(CreateProductDto))]
        [JsonSerializable(typeof(UpdateProductDto))]
        internal partial class ProbeJsonContext : JsonSerializerContext
        {
        }
        """;

    [Fact]
    public async Task Real_msbuild_build_must_close_crud_json_roots_in_the_same_compilation()
    {
        var repoRoot = FindRepoRoot();
        var probeDirectory = Path.Combine(repoRoot, "tests", "artifacts", "issue128-msbuild-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(probeDirectory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(probeDirectory, "Probe.csproj"),
                ProjectFile.Replace("__ROOT__", repoRoot.Replace('\\', '/')));
            await File.WriteAllTextAsync(Path.Combine(probeDirectory, "Declaration.cs"), DeclarationSource);
            await File.WriteAllTextAsync(Path.Combine(probeDirectory, "JsonRoots.cs"), JsonRootsSource);

            var buildResult = await RunDotnetAsync(
                repoRoot,
                $"build \"{Path.Combine(probeDirectory, "Probe.csproj")}\" -p:NuGetAudit=false --nologo --verbosity quiet",
                TimeSpan.FromMinutes(12));

            // Precondition: verify the producer actually runs and generates types. This separates
            // "producer didn't run" from "STJ can't see same-round output".
            var generatedFiles = Directory
                .EnumerateFiles(Path.Combine(probeDirectory, "obj"), "*.g.cs", SearchOption.AllDirectories)
                .Where(path => path.Contains("CrestCreates.CodeGenerator", StringComparison.Ordinal))
                .Select(File.ReadAllText)
                .ToList();
            generatedFiles.Should().NotBeEmpty("the CRUD generator must produce output for the probe declaration");
            var generatedContent = string.Join("\n", generatedFiles);
            generatedContent.Should().Contain("ProductAppService",
                "the producer must generate the service type (confirms generator ran)");

            // Designed behavior: the DTOs are materialized before compilation with the formal naming
            // contract (ProductDto from typeof(Product), not ProductAppServiceDto from the service class),
            // the authored official STJ roots resolve, and the same build closes them — the build must succeed.
            buildResult.ExitCode.Should().Be(0,
                "the formal CRUD declaration plus official STJ roots must close in one real build; observed build output:\n"
                + buildResult.Output);

            // The STJ source generator must actually own the DTO roots in the same build.
            var generatedContext = Directory
                .EnumerateFiles(Path.Combine(probeDirectory, "obj"), "*.g.cs", SearchOption.AllDirectories)
                .Where(path => path.Contains("System.Text.Json.SourceGeneration", StringComparison.Ordinal))
                .Select(File.ReadAllText)
                .ToList();
            generatedContext.Should().NotBeEmpty("the official STJ generator must produce context output for the probe roots");
            string.Join("\n", generatedContext).Should().Contain("ProductDto",
                "the generated STJ context must close the CRUD DTO roots in the same compilation");
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

    [Fact]
    public async Task Real_msbuild_producer_output_must_match_formal_naming_contract()
    {
        // Control test: verify the producer generates types with the formal naming (ProductDto from
        // typeof(Product)), not the legacy service-as-entity naming (ProductAppServiceDto). This
        // isolates the naming gap from the same-round STJ invisibility gap.
        var repoRoot = FindRepoRoot();
        var probeDirectory = Path.Combine(repoRoot, "tests", "artifacts", "issue128-msbuild-naming-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(probeDirectory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(probeDirectory, "Probe.csproj"),
                ProjectFile.Replace("__ROOT__", repoRoot.Replace('\\', '/')));
            await File.WriteAllTextAsync(Path.Combine(probeDirectory, "Declaration.cs"), DeclarationSource);

            var buildResult = await RunDotnetAsync(
                repoRoot,
                $"build \"{Path.Combine(probeDirectory, "Probe.csproj")}\" -p:NuGetAudit=false --nologo --verbosity quiet",
                TimeSpan.FromMinutes(12));

            // The build may fail for other reasons (mapping, legacy bundle), but we only check the
            // producer output naming here.
            var generatedFiles = Directory
                .EnumerateFiles(Path.Combine(probeDirectory, "obj"), "*.g.cs", SearchOption.AllDirectories)
                .Where(path => path.Contains("CrestCreates.CodeGenerator", StringComparison.Ordinal))
                .Select(File.ReadAllText)
                .ToList();
            generatedFiles.Should().NotBeEmpty("the CRUD generator must produce output");
            var generatedContent = string.Join("\n", generatedFiles);

            // Formal naming contract: DTOs named after the entity, in Contracts namespace
            generatedContent.Should().Contain("class ProductDto",
                "DTOs must be named after typeof(Product), not the service class");
            generatedContent.Should().Contain("namespace Shop.Application.Products.Contracts",
                "DTOs must be in the Contracts namespace per formal contract");
            generatedContent.Should().NotContain("class ProductAppServiceDto",
                "the service class must not be treated as the entity");
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
