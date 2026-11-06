using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.RepositoryGenerator;

/// <summary>
/// S4 G gate: a real MSBuild consumer project references the CodeGenerator analyzer and the real
/// framework attributes. Non-EF/unknown providers must fail the build with CCCREPO001/002 and must
/// not emit a direct SDK repository; the EF control must build and emit the EF repository surface.
/// </summary>
[Collection("CrudBuild")]
public sealed class Issue130ProviderDiagnosticsMsBuildTests
{
    private static string Root
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CrestCreates.slnx")))
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException("Repository root not found.");
        }
    }

    [Theory]
    [InlineData("FreeSql", "CCCREPO001")]
    [InlineData("Dapper", "CCCREPO002")]
    public async Task Non_ef_or_unknown_provider_must_fail_a_real_consumer_build(string provider, string diagnosticId)
    {
        await using var project = await ConsumerProject.Create(provider);
        var result = await project.Build();

        File.WriteAllText(Path.Combine(project.DirectoryPath, "build-output.txt"), result.Output);
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.Output.Contains(diagnosticId, StringComparison.Ordinal), result.Output);
        Assert.False(
            Directory.EnumerateFiles(project.DirectoryPath, "*Repository.g.cs", SearchOption.AllDirectories)
                .Any(path => path.Contains(provider + "OrderRepository", StringComparison.Ordinal)),
            "no direct SDK repository may be emitted");
    }

    [Fact]
    public async Task EfCore_consumer_build_remains_a_positive_control()
    {
        await using var project = await ConsumerProject.Create("EfCore");
        var result = await project.Build();

        File.WriteAllText(Path.Combine(project.DirectoryPath, "build-output.txt"), result.Output);
        Assert.True(result.ExitCode == 0, result.Output);
        var generated = string.Join(
            ", ",
            Directory.EnumerateFiles(project.DirectoryPath, "*.g.cs", SearchOption.AllDirectories)
                .Select(path => path.Replace(project.DirectoryPath, string.Empty)));
        Assert.True(generated.Contains("EfCoreOrderRepository", StringComparison.Ordinal), generated + " | " + result.Output);
    }

    private sealed class ConsumerProject : IAsyncDisposable
    {
        private ConsumerProject(string directory)
        {
            DirectoryPath = directory;
        }

        public string DirectoryPath { get; }

        public string ProjectFile => Path.Combine(DirectoryPath, "Consumer.csproj");

        public string GeneratedOutput => Path.Combine(DirectoryPath, "obj", "generated");

        public static async Task<ConsumerProject> Create(string provider)
        {
            var directory = Path.Combine(Root, "tests", "artifacts", "issue130-provider-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var project = new ConsumerProject(directory);
            var root = Root;
            var xml = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                    <CompilerGeneratedFilesOutputPath>obj/generated</CompilerGeneratedFilesOutputPath>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{root}/src/Tooling/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
                    <ProjectReference Include="{root}/src/Framework/Ddd/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj" />
                    <ProjectReference Include="{root}/src/Framework/Ddd/CrestCreates.Domain/CrestCreates.Domain.csproj" />
                    <ProjectReference Include="{root}/src/Framework/Ddd/CrestCreates.Application.Contracts/CrestCreates.Application.Contracts.csproj" />
                    <ProjectReference Include="{root}/src/Framework/Infrastructure/CrestCreates.Validation/CrestCreates.Validation.csproj" />
                    <ProjectReference Include="{root}/src/Framework/Infrastructure/CrestCreates.Aop/CrestCreates.Aop.csproj" />
                    <ProjectReference Include="{root}/src/Framework/Api/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj" />
                  </ItemGroup>
                </Project>
                """;
            await File.WriteAllTextAsync(project.ProjectFile, xml);
            await File.WriteAllTextAsync(Path.Combine(directory, "Order.cs"), """
                using CrestCreates.Domain.Shared.Attributes;
                using CrestCreates.Domain.Shared.Entities;

                namespace Consumer;

                [Entity(OrmProvider = "__PROVIDER__", GenerateAuditing = false, GeneratePermissions = false)]
                public class Order : IEntity<long>
                {
                    public long Id { get; set; }
                    public string Name { get; set; } = string.Empty;
                }
                """.Replace("__PROVIDER__", provider));
            return project;
        }

        public async Task<BuildResult> Build()
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = DirectoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add("build");
            start.ArgumentList.Add(ProjectFile);
            start.ArgumentList.Add("-p:NuGetAudit=false");
            start.ArgumentList.Add("-p:BuildInParallel=false");
            start.ArgumentList.Add("-m:1");
            start.ArgumentList.Add("--nologo");
            start.ArgumentList.Add("-v:q");
            start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                throw;
            }

            return new BuildResult(process.ExitCode, await stdout + await stderr);
        }

        public ValueTask DisposeAsync()
        {
            // Keep the directory when the build failed so the test output file survives for triage.
            if (File.Exists(Path.Combine(DirectoryPath, "build-output.txt")) &&
                !File.ReadAllText(Path.Combine(DirectoryPath, "build-output.txt")).Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                System.IO.Directory.Delete(DirectoryPath, recursive: true);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed record BuildResult(int ExitCode, string Output);
}
