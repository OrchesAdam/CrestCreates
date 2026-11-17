using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using CrestCreates.ModuleDiagnostics.Modules;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CrestCreates.BuildTasks.Tests;

public partial class ModuleManifestTests
{
    [Fact]
    public async System.Threading.Tasks.Task GeneratedLifecycle_IsIdempotentAndIsolatedPerComposition()
    {
        var result = await RunLifecycle("""
            var first = new ServiceCollection();
            var second = new ServiceCollection();
            first.AddModuleDiagnostics();
            second.AddModuleDiagnostics();
            ModuleAutoInitializer.RegisterAllModules(first);
            ModuleAutoInitializer.RegisterAllModules(first);
            ModuleAutoInitializer.RegisterAllModules(second);
            ModuleAutoInitializer.RegisterAllModules(second);
            using var firstProvider = first.BuildServiceProvider();
            using var secondProvider = second.BuildServiceProvider();
            using var firstHost = new TestHost(firstProvider);
            using var secondHost = new TestHost(secondProvider);
            await Task.WhenAll(ModuleAutoInitializer.InitializeModulesAsync(firstHost), ModuleAutoInitializer.InitializeModulesAsync(firstHost));
            await ModuleAutoInitializer.InitializeModulesAsync(secondHost);
            await ModuleAutoInitializer.InitializeModulesAsync(secondHost);
            var firstStore = firstProvider.GetRequiredService<IModuleDiagnosticsStore>();
            var secondStore = secondProvider.GetRequiredService<IModuleDiagnosticsStore>();
            return new[] { ProbeModule.Configured, first.Count(item => item.ServiceType == typeof(ProbeModule)),
                ProbeModule.PreInitialized, ProbeModule.Initialized, ProbeModule.PostInitialized, ProbeModule.AppInitialized,
                firstStore.TotalCount, secondStore.TotalCount, ReferenceEquals(firstStore, secondStore) ? 1 : 0 };
            """);

        result.Should().Equal(2, 1, 2, 2, 2, 2, 5, 5, 0);
    }

    [Fact]
    public async System.Threading.Tasks.Task GeneratedLifecycle_OverlappingInitializersSharePhaseState()
    {
        var result = await RunLifecycle("""
            var services = new ServiceCollection();
            ModuleAutoInitializer.RegisterAllModules(services);
            Other.ModuleAutoInitializer.RegisterAllModules(services);
            using var provider = services.BuildServiceProvider();
            using var host = new TestHost(provider);
            await Task.WhenAll(ModuleAutoInitializer.InitializeModulesAsync(host), Other.ModuleAutoInitializer.InitializeModulesAsync(host));
            return new[] { ProbeModule.Configured, ProbeModule.PreInitialized, ProbeModule.Initialized,
                ProbeModule.PostInitialized, ProbeModule.AppInitialized, provider.GetRequiredService<IModuleDiagnosticsStore>().TotalCount };
            """, secondInitializer: true);

        result.Should().Equal(1, 1, 1, 1, 1, 5);
    }

    [Fact]
    public async System.Threading.Tasks.Task GeneratedLifecycle_SameCollectionBuildsIndependentProviders()
    {
        var result = await RunLifecycle("""
            var services = new ServiceCollection();
            ModuleAutoInitializer.RegisterAllModules(services);
            using var first = services.BuildServiceProvider();
            using var second = services.BuildServiceProvider();
            using var firstHost = new TestHost(first);
            using var secondHost = new TestHost(second);
            await ModuleAutoInitializer.InitializeModulesAsync(firstHost);
            await ModuleAutoInitializer.InitializeModulesAsync(secondHost);
            return new[] { ProbeModule.Configured, ProbeModule.Initialized, first.GetRequiredService<IModuleDiagnosticsStore>().TotalCount,
                second.GetRequiredService<IModuleDiagnosticsStore>().TotalCount };
            """);

        result.Should().Equal(1, 2, 5, 5);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async System.Threading.Tasks.Task GeneratedLifecycle_CopiedCollectionOwnsConfigurationState(bool configureBeforeCopy, bool configureSourceFirst)
    {
        var body = """
            var first = new ServiceCollection();
            first.AddModuleDiagnostics();
            """ + (configureBeforeCopy ? "ModuleAutoInitializer.RegisterAllModules(first);\n" : "") + """
            IServiceCollection second = new ServiceCollection();
            foreach (var descriptor in first) second.Add(descriptor);
            """ + (configureSourceFirst
                ? "ModuleAutoInitializer.RegisterAllModules(first);\nModuleAutoInitializer.RegisterAllModules(second);\n"
                : "ModuleAutoInitializer.RegisterAllModules(second);\nModuleAutoInitializer.RegisterAllModules(first);\n") + """
            using var firstProvider = first.BuildServiceProvider();
            using var secondProvider = second.BuildServiceProvider();
            using var firstHost = new TestHost(firstProvider);
            using var secondHost = new TestHost(secondProvider);
            await ModuleAutoInitializer.InitializeModulesAsync(firstHost);
            await ModuleAutoInitializer.InitializeModulesAsync(secondHost);
            return new[] { ProbeModule.Configured, ProbeModule.Initialized,
                firstProvider.GetRequiredService<IModuleDiagnosticsStore>().TotalCount,
                secondProvider.GetRequiredService<IModuleDiagnosticsStore>().TotalCount };
            """;

        var result = await RunLifecycle(body);

        result.Should().Equal(configureBeforeCopy ? new[] { 1, 2, 5, 5 } : new[] { 2, 2, 5, 5 });
    }

    [Fact]
    public async System.Threading.Tasks.Task GeneratedLifecycle_ChainedCopiesInheritPublishedConfigurationFacts()
    {
        var result = await RunLifecycle("""
            var first = new ServiceCollection();
            first.AddModuleDiagnostics();
            ModuleAutoInitializer.RegisterAllModules(first);
            IServiceCollection second = new ServiceCollection();
            foreach (var descriptor in first) second.Add(descriptor);
            IServiceCollection third = new ServiceCollection();
            foreach (var descriptor in second) third.Add(descriptor);
            ModuleAutoInitializer.RegisterAllModules(third);
            using var firstProvider = first.BuildServiceProvider();
            using var secondProvider = second.BuildServiceProvider();
            using var thirdProvider = third.BuildServiceProvider();
            using var firstHost = new TestHost(firstProvider);
            using var secondHost = new TestHost(secondProvider);
            using var thirdHost = new TestHost(thirdProvider);
            await ModuleAutoInitializer.InitializeModulesAsync(firstHost);
            await ModuleAutoInitializer.InitializeModulesAsync(secondHost);
            await ModuleAutoInitializer.InitializeModulesAsync(thirdHost);
            return new[] { ProbeModule.Configured, ProbeModule.Initialized,
                firstProvider.GetRequiredService<IModuleDiagnosticsStore>().TotalCount,
                secondProvider.GetRequiredService<IModuleDiagnosticsStore>().TotalCount,
                thirdProvider.GetRequiredService<IModuleDiagnosticsStore>().TotalCount };
            """);

        result.Should().Equal(1, 3, 5, 5, 5);
    }

    [Fact]
    public async System.Threading.Tasks.Task GeneratedLifecycle_InitializationFailureIsNotReplayed()
    {
        var result = await RunLifecycle("""
            var services = new ServiceCollection();
            services.AddModuleDiagnostics();
            ModuleAutoInitializer.RegisterAllModules(services);
            using var provider = services.BuildServiceProvider();
            ProbeModule.FailInitialization = true;
            for (var attempt = 0; attempt < 2; attempt++) {
                try { await ModuleAutoInitializer.InitializeAllModulesAsync(provider); }
                catch (InvalidOperationException) { }
            }
            var store = provider.GetRequiredService<IModuleDiagnosticsStore>();
            return new[] { ProbeModule.PreInitialized, ProbeModule.Initialized, ProbeModule.PostInitialized, store.TotalCount, store.GetFailed().Count };
            """);

        result.Should().Equal(1, 1, 0, 3, 1);
    }

    [Fact]
    public async System.Threading.Tasks.Task GeneratedLifecycle_NestedModuleUsesOneDiagnosticIdentity()
    {
        var result = await RunLifecycle("""
            var services = new ServiceCollection();
            ModuleAutoInitializer.RegisterAllModules(services);
            using var provider = services.BuildServiceProvider();
            using var host = new TestHost(provider);
            await ModuleAutoInitializer.InitializeModulesAsync(host);
            var store = provider.GetRequiredService<IModuleDiagnosticsStore>();
            return new[] { store.GetByModule(typeof(ProbeModule).FullName!).Count,
                store.GetAll().Select(diagnostic => diagnostic.ModuleName).Distinct().Count() };
            """, nestedModule: true);

        result.Should().Equal(5, 1);
    }

    [Fact]
    public void ModuleOutputs_UnchangedContentPreservesTimestamps()
    {
        var input = FileWith("StableModule.cs", "using CrestCreates.Domain.Shared.Attributes; [CrestModule] public class StableModule {}");
        var manifest = Path.Combine(_directory, "stable.json");
        var scanner = Attach(new ScanModulesFromSource { SourceFiles = input, OutputPath = manifest });
        scanner.Execute().Should().BeTrue();
        var aggregate = Path.Combine(_directory, "aggregate.json");
        var collector = Attach(new CollectModuleManifests { ModuleManifestFiles = [new Microsoft.Build.Utilities.TaskItem(manifest)], OutputPath = aggregate });
        collector.Execute().Should().BeTrue();
        var initializer = Path.Combine(_directory, "stable.g.cs");
        var generator = Attach(new GenerateAggregatedModuleCode { ModuleManifestFile = aggregate, OutputPath = initializer, Namespace = "Fixture" });
        generator.Execute().Should().BeTrue();
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        foreach (var file in new[] { manifest, aggregate, initializer }) File.SetLastWriteTimeUtc(file, timestamp);

        scanner.Execute().Should().BeTrue();
        collector.Execute().Should().BeTrue();
        generator.Execute().Should().BeTrue();

        foreach (var file in new[] { manifest, aggregate, initializer }) File.GetLastWriteTimeUtc(file).Should().Be(timestamp, file);
    }

    private async System.Threading.Tasks.Task<int[]> RunLifecycle(string body, bool secondInitializer = false, bool nestedModule = false)
    {
        var module = Module(nestedModule ? "Fixture.Holder.ProbeModule" : "Fixture.ProbeModule", 0);
        module.Namespace = "Fixture";
        var manifest = FileWith("lifecycle.json", JsonSerializer.Serialize(new[] { module }));
        var output = Path.Combine(_directory, "lifecycle.g.cs");
        Attach(new GenerateAggregatedModuleCode { ModuleManifestFile = manifest, OutputPath = output, Namespace = "Fixture" }).Execute().Should().BeTrue();
        var source = """
            using System;
            using System.Linq;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;
            using CrestCreates.Modularity;
            using CrestCreates.ModuleDiagnostics.Modules;
            using CrestCreates.ModuleDiagnostics.Stores;
            namespace Fixture;
            public sealed class ProbeModule : ModuleBase {
                public static int Configured, PreInitialized, Initialized, PostInitialized, AppInitialized;
                public static bool FailInitialization;
                public override void OnConfigureServices(IServiceCollection services) => Configured++;
                public override Task OnPreInitializeAsync() { PreInitialized++; return Task.CompletedTask; }
                public override async Task OnInitializeAsync() { Initialized++; await Task.Yield(); if (FailInitialization) throw new InvalidOperationException("fixture failure"); }
                public override Task OnPostInitializeAsync() { PostInitialized++; return Task.CompletedTask; }
                public override Task OnApplicationInitializationAsync(IHost host) { AppInitialized++; return Task.CompletedTask; }
            }
            public sealed class TestHost(IServiceProvider services) : IHost {
                public IServiceProvider Services => services;
                public Task StartAsync(System.Threading.CancellationToken token = default) => Task.CompletedTask;
                public Task StopAsync(System.Threading.CancellationToken token = default) => Task.CompletedTask;
                public void Dispose() { }
            }
            public static class Probe {
                public static async Task<int[]> RunAsync() {
            """ + body + "\n} }";
        if (nestedModule)
            source = "using ProbeModule = Fixture.Holder.ProbeModule;\n" + source
                .Replace("public sealed class ProbeModule : ModuleBase", "public static class Holder { public sealed class ProbeModule : ModuleBase", StringComparison.Ordinal)
                .Replace("public sealed class TestHost", "} public sealed class TestHost", StringComparison.Ordinal);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(ModuleDiagnosticsServiceCollectionExtensions).Assembly.Location)
            .Append(typeof(CrestCreates.Modularity.ModuleBase).Assembly.Location)
            .Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path));
        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(File.ReadAllText(output)) };
        if (secondInitializer)
        {
            var other = Path.Combine(_directory, "other.g.cs");
            Attach(new GenerateAggregatedModuleCode { ModuleManifestFile = manifest, OutputPath = other, Namespace = "Other" }).Execute().Should().BeTrue();
            trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(other)));
        }
        var compilation = CSharpCompilation.Create("ModuleLifecycle" + Guid.NewGuid().ToString("N"),
            trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assembly = new MemoryStream();
        var emitted = compilation.Emit(assembly);
        emitted.Success.Should().BeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));
        assembly.Position = 0;
        var loaded = AssemblyLoadContext.Default.LoadFromStream(assembly);
        return await (System.Threading.Tasks.Task<int[]>)loaded.GetType("Fixture.Probe")!.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
    }
}
