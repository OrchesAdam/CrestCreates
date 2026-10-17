using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.CodeGenerator.CrudServiceGenerator;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

[CollectionDefinition("CrudBuild", DisableParallelization = true)]
public sealed class CrudBuildCollection { }

[Collection("CrudBuild")]
public class CrudBuildLifecycleTests
{
    [Fact]
    public async Task Clean_and_noop_builds_must_preserve_complete_outputs_and_repair_after_clean()
    {
        await using var p = await Project.Create();
        (await p.Build()).Success();
        var bundle = p.Read("CrestCreates.Crud.g.cs");
        var stamp = p.Time("CrestCreates.Crud.stamp");
        (await p.Build()).Success();
        p.Time("CrestCreates.Crud.stamp").Should().Be(stamp);
        p.Read("CrestCreates.Crud.g.cs").Should().Be(bundle);
        (await p.Run("clean", p.File)).Success();
        System.IO.File.Exists(p.Artifact("CrestCreates.Crud.g.cs")).Should().BeFalse();
        System.IO.File.Exists(p.Artifact("CrestCreates.Crud.outputs.json")).Should().BeFalse();
        (await p.Build()).Success();
        p.Read("CrestCreates.Crud.g.cs").Should().Be(bundle);
    }

    [Theory]
    [InlineData("CrestCreates.Crud.g.cs")]
    [InlineData("CrestCreates.Crud.outputs.json")]
    public async Task Missing_output_must_be_repaired_even_when_stamp_exists(string artifact)
    {
        await using var p = await Project.Create();
        (await p.Build()).Success();
        var original = p.Read(artifact);
        System.IO.File.Move(p.Artifact(artifact), p.Artifact(artifact) + ".review-backup");
        (await p.Build()).Success();
        p.Read(artifact).Should().Be(original);
    }

    [Fact]
    public async Task Modified_manifest_must_be_reconciled_before_generators_consume_ownership()
    {
        await using var p = await Project.Create();
        (await p.Build()).Success();
        var original = p.Read("CrestCreates.Crud.outputs.json");
        await System.IO.File.WriteAllTextAsync(p.Artifact("CrestCreates.Crud.outputs.json"),
            original.Replace("Shop.Application.Products.ProductAppService", "Other.Owner.Service"));
        (await p.Build()).Success();
        p.Read("CrestCreates.Crud.outputs.json").Should().Be(original);
    }

    [Fact]
    public async Task Content_change_with_preserved_timestamp_and_source_rename_must_invalidate_inputs()
    {
        await using var p = await Project.Create();
        (await p.Build()).Success();
        var path = Path.Combine(p.Directory, "Declaration.cs");
        var timestamp = System.IO.File.GetLastWriteTimeUtc(path);
        var source = await System.IO.File.ReadAllTextAsync(path);
        await System.IO.File.WriteAllTextAsync(path, source.Replace("public decimal Price", "public string Extra { get; set; } = string.Empty;\n                public decimal Price"));
        System.IO.File.SetLastWriteTimeUtc(path, timestamp);
        (await p.Build()).Success();
        p.Read("CrestCreates.Crud.g.cs").Should().Contain("Extra");
        System.IO.File.Move(path, Path.Combine(p.Directory, "Renamed.cs"));
        (await p.Build()).Success();
        p.Read("CrestCreates.Crud.inputs.json").Should().Contain("Renamed.cs").And.NotContain("Declaration.cs");
    }

    [Fact]
    public async Task Removing_declaration_must_remove_all_owned_types_and_json_roots()
    {
        await using var p = await Project.Create();
        (await p.Build()).Success();
        var source = CrudJsonContractMsBuildBaselineTests.DeclarationSource;
        source = source.Replace("[GenerateCrudService(typeof(Shop.Domain.Entities.Product), RoutePrefix = \"/api/products\")]", "");
        await System.IO.File.WriteAllTextAsync(Path.Combine(p.Directory, "Declaration.cs"), source);
        (await p.Build()).Success();
        p.Read("CrestCreates.Crud.g.cs").Should().NotContain("ProductDto");
        p.Read("CrestCreates.JsonContracts.g.cs").Should().NotContain("CrestCreatesCrudJsonSerializerContext");
        using var manifest = JsonDocument.Parse(p.Read("CrestCreates.Crud.outputs.json"));
        manifest.RootElement.GetProperty("Entries").GetArrayLength().Should().Be(0);
        (await p.Build("-p:CrestCreatesCodeGeneration=false")).Success();
    }

    [Theory]
    [InlineData("-p:CrestCreatesCodeGeneration=false", "CCCRUD002")]
    [InlineData("-p:CrestCreatesCrudModelSchemaVersion=unknown-schema", "CCCRUD006")]
    public async Task Invalid_configuration_must_fail_without_reusing_a_success_stamp(string option, string code)
    {
        await using var p = await Project.Create();
        (await p.Build()).Success();
        var previousStamp = p.Time("CrestCreates.Crud.stamp");
        var failed = await p.Build(option);
        failed.ExitCode.Should().NotBe(0);
        failed.Output.Should().Contain(code);
        if (System.IO.File.Exists(p.Artifact("CrestCreates.Crud.stamp")))
            p.Time("CrestCreates.Crud.stamp").Should().Be(previousStamp, "a failed build must not record a new success");
        (await p.Build()).Success();
    }

    [Fact]
    public async Task Ready_marker_without_owned_materialized_surface_must_fail_closed()
    {
        await using var p = await Project.Create();
        var xml = await System.IO.File.ReadAllTextAsync(p.File);
        xml = xml.Replace(CrudJsonContractMsBuildBaselineTests.ToolchainImports.Replace("__ROOT__", Project.Root), "")
            .Replace("</Project>", "<PropertyGroup><CrestCreatesCrudJsonToolchain>true</CrestCreatesCrudJsonToolchain></PropertyGroup><ItemGroup><CompilerVisibleProperty Include=\"CrestCreatesCrudJsonToolchain\" /></ItemGroup></Project>");
        await System.IO.File.WriteAllTextAsync(p.File, xml);
        var result = await p.Build();
        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("CCCRUD002");
    }

    [Fact]
    public async Task Design_time_must_generate_current_isolated_surface_and_never_reuse_runtime_stamp()
    {
        await using var p = await Project.Create();
        (await p.DesignTime()).Success();
        var design = Path.Combine(p.Intermediate, "design-time");
        System.IO.File.ReadAllText(Path.Combine(design, "CrestCreates.Crud.g.cs")).Should().Contain("ProductDto");
        System.IO.File.ReadAllText(Path.Combine(design, "CrestCreates.JsonContracts.g.cs")).Should().Contain("CrestCreatesCrudJsonSerializerContext");
        System.IO.File.Exists(p.Artifact("CrestCreates.Crud.stamp")).Should().BeFalse();
        (await p.Build()).Success();
        var runtimeBundle = p.Read("CrestCreates.Crud.g.cs");
        var runtimeStamp = p.Time("CrestCreates.Crud.stamp");
        var path = Path.Combine(p.Directory, "Declaration.cs");
        await System.IO.File.WriteAllTextAsync(path, (await System.IO.File.ReadAllTextAsync(path)).Replace("public decimal Price", "public int DesignOnly {get;set;}\n                public decimal Price"));
        (await p.DesignTime()).Success();
        System.IO.File.ReadAllText(Path.Combine(design, "CrestCreates.Crud.g.cs")).Should().Contain("DesignOnly");
        p.Read("CrestCreates.Crud.g.cs").Should().Be(runtimeBundle);
        p.Time("CrestCreates.Crud.stamp").Should().Be(runtimeStamp);
        (await p.Build()).Success();
        p.Read("CrestCreates.Crud.g.cs").Should().Contain("DesignOnly");
    }

    [Fact]
    public async Task Target_framework_and_runtime_identifier_outputs_must_be_isolated()
    {
        await using var p = await Project.Create(external: true);
        await p.UseBinaryReferences(package: false);
        var xml = await System.IO.File.ReadAllTextAsync(p.File);
        await System.IO.File.WriteAllTextAsync(p.File, xml.Replace("<TargetFramework>net10.0</TargetFramework>",
            "<TargetFramework></TargetFramework><TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks><EnableWindowsTargeting>true</EnableWindowsTargeting>"));
        (await p.Build()).Success();
        var first = p.Read("CrestCreates.Crud.inputs.json");
        var windows = System.IO.File.ReadAllText(Path.Combine(p.Directory, "obj", "Debug", "net10.0-windows", "CrestCreates.Crud.inputs.json"));
        first.Should().Contain("net10.0");
        windows.Should().Contain("net10.0-windows");
        (await p.Build("-f", "net10.0", "-r", "linux-x64", "--self-contained", "false")).Success();
        (await p.Build("-f", "net10.0", "-r", "linux-arm64", "--self-contained", "false")).Success();
        foreach (var rid in new[] { "linux-x64", "linux-arm64" })
            System.IO.File.ReadAllText(Path.Combine(p.Intermediate, rid, "CrestCreates.Crud.inputs.json"))
                .Should().Contain("\"runtimeIdentifier\":\"" + rid + "\"");
        p.Read("CrestCreates.Crud.inputs.json").Should().Be(first);
    }

    [Theory]
    [InlineData("CrestCrudBundleFile")]
    [InlineData("CrestCrudInputManifest")]
    [InlineData("CrestCrudOutputsManifest")]
    [InlineData("CrestCrudGenerationStamp")]
    [InlineData("CrestCrudTemporaryDirectory")]
    public async Task Output_path_with_only_a_matching_prefix_must_be_rejected(string property)
    {
        await using var p = await Project.Create();
        var result = await p.Build("-p:" + property + "=" + p.Intermediate + "-outside/escaped");
        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("CCCRUD006");
        System.IO.File.Exists(p.Intermediate + "-outside/escaped").Should().BeFalse();
    }

    [Fact]
    public async Task Packaged_toolchain_must_build_a_fresh_external_consumer_without_repository_imports()
    {
        await using var p = await Project.Create(external: true);
        var feed = Path.Combine(p.Directory, "feed");
        var pack = await p.Run("pack", Path.Combine(Project.Root, "src/Tooling/CrestCreates.JsonContracts.BuildTasks/CrestCreates.JsonContracts.BuildTasks.csproj"),
            "-o", feed, "-p:PackageVersion=1.0.0", "-p:NuGetAudit=false", "-m:1");
        pack.Success();
        await p.UseBinaryReferences(package: true);
        (await p.Build()).Success();
        p.Read("CrestCreates.JsonContracts.g.cs").Should().Contain("CrestCreatesCrudJsonSerializerContext");
        var stj = System.IO.Directory.EnumerateFiles(Path.Combine(p.Directory, "obj"), "*.g.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains("System.Text.Json.SourceGeneration", StringComparison.Ordinal)).Select(System.IO.File.ReadAllText);
        string.Join("\n", stj).Should().Contain("JsonTypeInfo<global::Shop.Application.Products.Contracts.ProductDto>");
        (await System.IO.File.ReadAllTextAsync(p.File)).Should().NotContain(Project.Root);
    }

    private sealed record Result(int ExitCode, string Output)
    {
        public void Success() => ExitCode.Should().Be(0, Output);
    }
    private sealed class Project : IAsyncDisposable
    {
        public static string Root
        {
            get
            {
                for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                    if (System.IO.File.Exists(Path.Combine(dir.FullName, "CrestCreates.slnx"))) return dir.FullName;
                throw new InvalidOperationException("Repository not found.");
            }
        }
        public string Directory {get; private init;} = "";
        public string File => Path.Combine(Directory, "Probe.csproj");
        public string Intermediate => Path.Combine(Directory, "obj", "Debug", "net10.0");
        public string Artifact(string name) => Path.Combine(Intermediate, name);
        public string Read(string name) => System.IO.File.ReadAllText(Artifact(name));
        public DateTime Time(string name) => System.IO.File.GetLastWriteTimeUtc(Artifact(name));
        public static async Task<Project> Create(bool external = false)
        {
            var p = new Project {Directory = Path.Combine(external ? Path.GetTempPath() : Path.Combine(Root, "tests", "artifacts"), "crud-lifecycle-"+Guid.NewGuid().ToString("N"))};
            System.IO.Directory.CreateDirectory(p.Directory);
            var xml = CrudJsonContractMsBuildBaselineTests.ProjectFile.Replace("__ROOT__", Root)
                .Replace("__IMPORTS__", CrudJsonContractMsBuildBaselineTests.ToolchainImports.Replace("__ROOT__", Root))
                .Replace("</Project>", "<PropertyGroup><PublishTrimmed>false</PublishTrimmed><PublishAot>false</PublishAot><EnableTrimAnalyzer>false</EnableTrimAnalyzer></PropertyGroup></Project>");
            await System.IO.File.WriteAllTextAsync(p.File, xml);
            await System.IO.File.WriteAllTextAsync(Path.Combine(p.Directory, "Declaration.cs"), CrudJsonContractMsBuildBaselineTests.DeclarationSource);
            return p;
        }
        public async Task UseBinaryReferences(bool package)
        {
            var lib = Path.Combine(Directory, "lib");
            System.IO.Directory.CreateDirectory(lib);
            var assemblies = System.IO.Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
                .Where(path => Path.GetFileName(path).StartsWith("CrestCreates.", StringComparison.Ordinal)
                    || Path.GetFileName(path) is "FluentValidation.dll" or "Rougamo.dll").ToArray();
            foreach (var assembly in assemblies) System.IO.File.Copy(assembly, Path.Combine(lib, Path.GetFileName(assembly)), true);
            System.IO.File.Copy(typeof(CrudServiceSourceGenerator).Assembly.Location, Path.Combine(lib, "CrestCreates.CodeGenerator.dll"), true);
            var xml = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
                    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                    <CompilerGeneratedFilesOutputPath>obj/$(Configuration)/$(TargetFramework)/source-generators</CompilerGeneratedFilesOutputPath>
                    <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
                    <RestorePackagesPath>packages</RestorePackagesPath><RestoreSources>feed</RestoreSources></PropertyGroup>
                  <ItemGroup><PackageReference Include="CrestCreates.JsonContracts.Build" Version="1.0.0" />
                    <FrameworkReference Include="Microsoft.AspNetCore.App" />
                    <Analyzer Include="lib/CrestCreates.CodeGenerator.dll" />
                    <Reference Include="lib/*.dll" Exclude="lib/CrestCreates.CodeGenerator.dll" /></ItemGroup>
                </Project>
                """;
            if (!package)
                xml = xml.Replace("<PackageReference Include=\"CrestCreates.JsonContracts.Build\" Version=\"1.0.0\" />", "")
                    .Replace("<RestoreSources>feed</RestoreSources>", "")
                    .Replace("</Project>", CrudJsonContractMsBuildBaselineTests.ToolchainImports.Replace("__ROOT__", Root) + "</Project>");
            await System.IO.File.WriteAllTextAsync(File, xml);
        }
        public async Task<Result> DesignTime()
        {
            (await Run("restore", File)).Success();
            return await Run("msbuild", File, "-t:Compile", "-p:DesignTimeBuild=true",
                "-p:BuildingInsideVisualStudio=true", "-p:SkipCompilerExecution=true");
        }
        public Task<Result> Build(params string[] options) => Run(new[] {"build", File}.Concat(options).ToArray());
        public async Task<Result> Run(params string[] args)
        {
            var start = new ProcessStartInfo("dotnet") {WorkingDirectory=Directory,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
            foreach (var arg in args) start.ArgumentList.Add(arg);
            start.ArgumentList.Add("-p:NuGetAudit=false"); start.ArgumentList.Add("-p:BuildInParallel=false");
            start.ArgumentList.Add("-m:1"); start.ArgumentList.Add("--nologo"); start.ArgumentList.Add("-v:q");
            start.Environment["DOTNET_CLI_UI_LANGUAGE"]="en";
            start.Environment["MSBUILDDISABLENODEREUSE"]="1";
            using var process=Process.Start(start)!;
            var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try {await process.WaitForExitAsync(timeout.Token);} catch(OperationCanceledException){process.Kill(true);throw;}
            return new Result(process.ExitCode, await stdout+await stderr);
        }
        public ValueTask DisposeAsync() {System.IO.Directory.Delete(Directory,true);return ValueTask.CompletedTask;}
    }
}
