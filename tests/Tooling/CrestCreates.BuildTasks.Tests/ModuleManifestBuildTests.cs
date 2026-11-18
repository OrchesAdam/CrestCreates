using System.Diagnostics;
using System.Security;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.BuildTasks.Tests;

public partial class ModuleManifestTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task ReleaseBuild_EvaluatedCustomPaths_TransitiveModulesAndRemoval(bool packaged)
    {
        var root = FindRoot();
        var tooling = Path.Combine(root, "src/Tooling/CrestCreates.BuildTasks");
        var import = Path.Combine(tooling, "CrestCreates.Modules.props");
        if (packaged)
        {
            var packageBuild = Path.Combine(_directory, "package/build");
            Directory.CreateDirectory(packageBuild);
            foreach (var name in new[] { "CrestCreates.BuildTasks.props", "CrestCreates.BuildTasks.targets" })
                File.Copy(Path.Combine(tooling, "build", name), Path.Combine(packageBuild, name));
            File.Copy(import, Path.Combine(packageBuild, "CrestCreates.Modules.props"));
            import = Path.Combine(packageBuild, "CrestCreates.BuildTasks.targets");
        }
        FileWith("Directory.Build.props", """
            <Project><PropertyGroup>
              <BaseIntermediateOutputPath>custom-obj/</BaseIntermediateOutputPath>
              <IntermediateOutputPath>$(BaseIntermediateOutputPath)$(Configuration)/special/</IntermediateOutputPath>
              <DefaultItemExcludes>$(DefaultItemExcludes);custom-obj/**</DefaultItemExcludes>
            </PropertyGroup></Project>
            """);
        // A project without the manifest contract is a legitimate ordinary reference.
        var contracts = CreateProject("Contracts", null, null);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(contracts)!, "Contracts.cs"), ContractsSource);
        var leaf = CreateProject("Leaf", import, contracts);
        var source = Path.Combine(Path.GetDirectoryName(leaf)!, "Modules.cs");
        File.WriteAllText(source, """
            extern alias contracts;
            using CM = contracts::CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;
            using CrestCreates.Domain.Shared.Attributes;
            namespace @Fixture;
            public static class ModuleOptions { public const int FirstOrder = -2; public const bool AutoRegister = false; }
            public class Parent : global::CrestCreates.Modularity.ModuleBase {}
            [CM] public class Dep : Parent {}
            [global::CrestCreates.Domain.Shared.Attributes.CrestModule(typeof(Dep), Order = ModuleOptions.FirstOrder, AutoRegisterServices = ModuleOptions.AutoRegister)]
            public class First : Parent {}
            public class NestedModules { [CM] public class Dep : Parent {} }
            public class DerivedAttributeContainer : AttributeParent {
                [CrestModule] public class Ordinary {}
            }
            [CrestCreates.Domain.Shared.Attributes.CrestModule] public class RelativeOrdinary {}
            """);
        var middle = CreateProject("Middle", import, leaf);
        var host = CreateProject("Host", import, middle);
        var first = await RunBuild(host, packaged ? "package-first" : "source-first");
        first.ExitCode.Should().Be(0, first.Log);
        var leafManifest = Path.Combine(Path.GetDirectoryName(leaf)!, "custom-obj/Release/special/net10.0/ModuleManifest.json");
        File.Exists(leafManifest).Should().BeTrue();
        var leafModules = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(leafManifest))!;
        leafModules.Select(m => m.FullName).Should().Equal("Fixture.Dep", "Fixture.First", "Fixture.NestedModules.Dep");
        var firstModule = leafModules.Single(module => module.Name == "First");
        firstModule.DependsOn.Should().Equal("Fixture.Dep");
        firstModule.Order.Should().Be(-2);
        firstModule.AutoRegisterServices.Should().BeFalse();
        var hostObj = Path.Combine(Path.GetDirectoryName(host)!, "custom-obj/Release/special/net10.0");
        var aggregate = Path.Combine(hostObj, "AggregatedModuleManifest.json");
        JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(aggregate))!.Select(m => m.FullName)
            .Should().Equal("Fixture.Dep", "Fixture.First", "Fixture.NestedModules.Dep");
        var initializer = Path.Combine(hostObj, "ModuleAutoInitializer.g.cs");
        var code = File.ReadAllText(initializer);
        code.Split("services.TryAddSingleton<global::Fixture.Dep>();").Length.Should().Be(2);
        code.Split("services.TryAddSingleton<global::Fixture.First>();").Length.Should().Be(2);
        code.IndexOf("services.TryAddSingleton<global::Fixture.Dep>", StringComparison.Ordinal).Should().BeLessThan(code.IndexOf("services.TryAddSingleton<global::Fixture.First>", StringComparison.Ordinal));
        Directory.GetDirectories(_directory, "Debug", SearchOption.AllDirectories).Should().BeEmpty();

        var stableTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        foreach (var output in new[] { leafManifest, aggregate, initializer }) File.SetLastWriteTimeUtc(output, stableTime);
        var unchanged = await RunBuild(host, packaged ? "package-unchanged" : "source-unchanged");
        unchanged.ExitCode.Should().Be(0, unchanged.Log);
        foreach (var output in new[] { leafManifest, aggregate, initializer }) File.GetLastWriteTimeUtc(output).Should().Be(stableTime, output);

        var originalSource = File.ReadAllText(source);
        File.WriteAllText(source, originalSource + "\n#if R324_ENABLED\n[CM] public class ConditionalModule : Parent {}\n#endif\n");
        FileWith("Directory.Build.targets", "<Project><PropertyGroup><DefineConstants>$(DefineConstants);R324_ENABLED</DefineConstants></PropertyGroup></Project>");
        var conditional = await RunBuild(host, packaged ? "package-conditional" : "source-conditional");
        conditional.ExitCode.Should().Be(0, conditional.Log);
        JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(aggregate))!
            .Select(module => module.FullName).Should().Contain("Fixture.ConditionalModule");

        FileWith("Directory.Build.targets", "<Project />");
        var imported = await RunBuild(host, packaged ? "package-imported" : "source-imported");
        imported.ExitCode.Should().Be(0, imported.Log);
        JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(aggregate))!
            .Select(module => module.FullName).Should().NotContain("Fixture.ConditionalModule");

        File.WriteAllText(source, originalSource.Replace("ModuleOptions.FirstOrder", "ReferenceOptions.Order", StringComparison.Ordinal));
        var contractSource = Path.Combine(Path.GetDirectoryName(contracts)!, "Contracts.cs");
        File.AppendAllText(contractSource, "\nnamespace Fixture { public static class ReferenceOptions { public const int Order = 11; } }\n");
        var referenceAdded = await RunBuild(host, packaged ? "package-reference-added" : "source-reference-added");
        referenceAdded.ExitCode.Should().Be(0, referenceAdded.Log);
        JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(leafManifest))!
            .Single(module => module.Name == "First").Order.Should().Be(11);
        File.WriteAllText(contractSource, File.ReadAllText(contractSource).Replace("Order = 11", "Order = 12", StringComparison.Ordinal));
        var referenceChanged = await RunBuild(host, packaged ? "package-reference-changed" : "source-reference-changed");
        referenceChanged.ExitCode.Should().Be(0, referenceChanged.Log);
        JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(aggregate))!
            .Single(module => module.Name == "First").Order.Should().Be(12);

        // An ordinary rebuild must replace every stale manifest and initializer.
        File.WriteAllText(source, "namespace Fixture; public class Ordinary {}");
        var second = await RunBuild(host, packaged ? "package-empty" : "source-empty");
        second.ExitCode.Should().Be(0, second.Log);
        File.ReadAllText(leafManifest).Trim().Should().Be("[]");
        File.ReadAllText(aggregate).Trim().Should().Be("[]");
        File.ReadAllText(initializer).Should().Contain("RegisterAllModules").And.NotContain("Fixture.Dep").And.NotContain("Fixture.First");

        // Ordinary unbound attributes are rejected by CoreCompile, not preempted by the scanner.
        File.WriteAllText(source, "using CrestCreates.Domain.Shared.Attributes;\n[MissingModule]\npublic class UnboundModule {}\n");
        var unbound = await RunBuild(host, packaged ? "package-unbound" : "source-unbound");
        unbound.ExitCode.Should().NotBe(0, unbound.Log);
        unbound.Log.Should().Contain("CS0246").And.Contain("Modules.cs").And.NotContain("CCMB001");
        File.ReadAllText(leafManifest).Trim().Should().Be("[]");

        // An unbound formal module attribute candidate still fails closed before any manifest write.
        File.WriteAllText(source, "[CrestModule]\npublic class UnboundModule {}\n");
        File.WriteAllText(leafManifest, "stale-unbound");
        var unboundFormal = await RunBuild(host, packaged ? "package-unbound-formal" : "source-unbound-formal");
        unboundFormal.ExitCode.Should().NotBe(0, unboundFormal.Log);
        unboundFormal.Log.Should().Contain("CCMB001").And.Contain("Modules.cs");
        File.ReadAllText(leafManifest).Should().Be("stale-unbound");
        File.WriteAllText(source, "namespace Fixture; public class Ordinary {}");

        // Exercise fail-closed behavior through MSBuild, not just direct task calls.
        var failureTarget = Path.Combine(_directory, "Failure.targets");
        foreach (var failure in new[] { "cycle", "corrupt", "missing" })
        {
            var manifest = failure switch
            {
                "cycle" => JsonSerializer.Serialize(new[] { Module("Fixture.A", 0, "Fixture.B"), Module("Fixture.B", 0, "Fixture.A") }),
                "corrupt" => "{",
                _ => "[]"
            };
            var file = FileWith("required.json", manifest);
            File.WriteAllText(failureTarget, $"""
                <Project><Target Name="InjectRequiredManifest" BeforeTargets="CollectAndGenerateAggregatedModules">
                  <ItemGroup><_ModuleManifestFiles Include="{SecurityElement.Escape(failure == "missing" ? file + ".missing" : file)}" /></ItemGroup>
                </Target></Project>
                """);
            var failed = await RunBuild(host, $"{(packaged ? "package" : "source")}-{failure}", failureTarget);
            failed.ExitCode.Should().NotBe(0, failed.Log);
            failed.Log.Should().Contain(failure == "cycle" ? "Circular module dependency" : failure == "corrupt" ? "JSON" : "required.json.missing");
        }

        string CreateProject(string name, string? targets, string? reference)
        {
            var directory = Path.Combine(_directory, name);
            Directory.CreateDirectory(directory);
            var project = Path.Combine(directory, name + ".csproj");
            File.WriteAllText(project, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  {(packaged && targets != null ? $"<Import Project=\"{SecurityElement.Escape(Path.Combine(Path.GetDirectoryName(targets)!, "CrestCreates.BuildTasks.props"))}\" />" : "")}
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableEntityPermissionsScan>false</EnableEntityPermissionsScan>
                    <CrestCreatesBuildTasksAssembly>{SecurityElement.Escape(typeof(ScanModulesFromSource).Assembly.Location)}</CrestCreatesBuildTasksAssembly>
                  </PropertyGroup>
                  <ItemGroup>
                    <FrameworkReference Include="Microsoft.AspNetCore.App" />
                    <Reference Include="CrestCreates.Modularity"><HintPath>{SecurityElement.Escape(typeof(CrestCreates.Modularity.ModuleBase).Assembly.Location)}</HintPath></Reference>
                    <Reference Include="CrestCreates.ModuleDiagnostics"><HintPath>{SecurityElement.Escape(typeof(CrestCreates.ModuleDiagnostics.Modules.ModuleInitializationState).Assembly.Location)}</HintPath></Reference>
                  </ItemGroup>
                  {(reference == null ? "" : $"<ItemGroup><ProjectReference Include=\"{SecurityElement.Escape(reference)}\" Aliases=\"global,contracts\" /></ItemGroup>")}
                  {(targets == null ? "" : $"<Import Project=\"{SecurityElement.Escape(targets)}\" />")}
                  <Import Project="$(R323FailureTargets)" Condition="'$(R323FailureTargets)' != '' and '$(MSBuildProjectName)' == 'Host'" />
                </Project>
                """);
            return project;
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task EmptyManifest_PlainClassLibrary_RequiresNoRuntimeReferences()
    {
        var root = FindRoot();
        var import = Path.Combine(root, "src/Tooling/CrestCreates.BuildTasks/CrestCreates.Modules.props");
        var directory = Path.Combine(_directory, "Plain");
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "Plain.csproj");
        File.WriteAllText(project, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableEntityPermissionsScan>false</EnableEntityPermissionsScan>
                <CrestCreatesBuildTasksAssembly>{SecurityElement.Escape(typeof(ScanModulesFromSource).Assembly.Location)}</CrestCreatesBuildTasksAssembly>
              </PropertyGroup>
              <Import Project="{SecurityElement.Escape(import)}" />
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, "Class1.cs"), "namespace Plain; public sealed class Class1 {}\n");

        var result = await RunBuild(project, "plain-no-runtime");

        result.ExitCode.Should().Be(0, result.Log);
        var initializer = Path.Combine(directory, "obj/Release/net10.0/ModuleAutoInitializer.g.cs");
        var code = File.ReadAllText(initializer);
        code.Should().Contain("RegisterAllModules").And.NotContain("IServiceCollection")
            .And.NotContain("ModuleDiagnosticsStore").And.NotContain("IHost");
    }

    private async System.Threading.Tasks.Task<(int ExitCode, string Log)> RunBuild(string project, string name, string? failureTargets = null)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnet)) dotnet = Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "/home/orches/.dotnet", "dotnet");
        var start = new ProcessStartInfo(dotnet) { WorkingDirectory = _directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "build", project, "-c", "Release", "-m:1", "-p:UseSharedCompilation=false", "--disable-build-servers", "-v:minimal" }) start.ArgumentList.Add(argument);
        if (failureTargets != null) start.ArgumentList.Add("-p:R323FailureTargets=" + failureTargets);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2)); }
        catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        var log = await stdout + await stderr;
        File.WriteAllText(Path.Combine(_directory, name + ".log"), log);
        return (process.ExitCode, log);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CrestCreates.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found");
    }

    private const string ContractsSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        namespace CrestCreates.Domain.Shared.Attributes {
          [AttributeUsage(AttributeTargets.Class)]
          public class CrestModuleAttribute : Attribute {
            public CrestModuleAttribute(params Type[] dependsOn) { DependsOn = dependsOn; }
            public Type[] DependsOn { get; set; }
            public int Order { get; set; }
            public bool AutoRegisterServices { get; set; } = true;
          }
        }
        namespace Fixture {
          public class AttributeParent { public class CrestModuleAttribute : Attribute {} }
        }
        namespace Fixture.CrestCreates.Domain.Shared.Attributes {
          public class CrestModuleAttribute : Attribute {}
        }
        """;
}
