using System.Text.Json;
using FluentAssertions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Moq;
using Xunit;

namespace CrestCreates.BuildTasks.Tests;

[CollectionDefinition("Module manifests", DisableParallelization = true)]
public class ModuleManifestCollection;

[Collection("Module manifests")]
public partial class ModuleManifestTests
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "r323-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IBuildEngine> _engine = new();

    public ModuleManifestTests() => Directory.CreateDirectory(_directory);

    private string FileWith(string name, string text)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private T Attach<T>(T task) where T : Microsoft.Build.Utilities.Task
    {
        task.BuildEngine = _engine.Object;
        return task;
    }

    [Fact]
    public void Scan_AllDeclarationsAndAttributeForms_IgnoresCommentsAndStrings()
    {
        var source = FileWith("Modules.cs", """
            using CM = CrestCreates.Domain.Shared.CrestModuleAttribute;
            namespace Ns;
            // [CrestModule] class Fake : ModuleBase {}
            class Parent : ModuleBase { string fake = "[CrestModule] class Fake : ModuleBase"; }
            [CM(typeof(global::Ns.Dep), Order = -2, AutoRegisterServices = false)]
            class First : Parent {}
            [global::CrestCreates.Domain.Shared.CrestModule(DependsOn = new[] { typeof(Ns.First) })]
            class Second : Parent {}
            [CrestCreates.Domain.Shared.CrestModule] class Dep : Parent {}
            """);
        var output = FileWith("manifest.json", "stale");
        Attach(new ScanModulesFromSource { SourceFiles = source, OutputPath = output }).Execute().Should().BeTrue();
        var modules = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!;
        modules.Select(m => m.FullName).Should().Equal("Ns.Dep", "Ns.First", "Ns.Second");
        var first = modules.Single(m => m.Name == "First");
        first.Namespace.Should().Be("Ns");
        first.DependsOn.Should().Equal("Ns.Dep");
        first.Order.Should().Be(-2);
        first.AutoRegisterServices.Should().BeFalse();
        modules.Single(m => m.Name == "Second").DependsOn.Should().Equal("Ns.First");
    }

    [Fact]
    public void Scan_ZeroModules_ReplacesStaleManifest()
    {
        var output = FileWith("manifest.json", "stale");
        Attach(new ScanModulesFromSource { SourceFiles = FileWith("None.cs", "class Ordinary {}"), OutputPath = output }).Execute().Should().BeTrue();
        File.ReadAllText(output).Trim().Should().Be("[]");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scan_UsesEvaluatedPreprocessorSymbols(bool constantsSupplied)
    {
        var source = FileWith("Conditional.cs", """
            #if !KEEP
            #error Scanner must receive KEEP
            #endif
            [CrestModule] class ConditionalModule {}
            """);
        var output = FileWith("conditional-manifest.json", "stale");
        var task = Attach(new ScanModulesFromSource
        {
            SourceFiles = source,
            DefineConstants = constantsSupplied ? "TRACE;KEEP" : string.Empty,
            OutputPath = output
        });

        task.Execute().Should().Be(constantsSupplied);
        if (constantsSupplied)
        {
            var modules = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!;
            modules.Select(m => m.FullName).Should().Equal("ConditionalModule");
        }
        else
        {
            _engine.Verify(e => e.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()), Times.AtLeastOnce);
        }
    }

    [Theory]
    [InlineData("[CrestModule(Order = wrong)] class M {}")]
    [InlineData("[CrestModule(AutoRegisterServices = 1)] class M {}")]
    [InlineData("[CrestModule] class M {")]
    [InlineData("[CrestModule] class M {} [CrestModule] class M {}")]
    public void Scan_InvalidDeclarations_Fail(string source)
    {
        Attach(new ScanModulesFromSource { SourceFiles = FileWith("Bad.cs", source), OutputPath = Path.Combine(_directory, "out.json") }).Execute().Should().BeFalse();
        _engine.Verify(e => e.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()), Times.AtLeastOnce);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[{}]")]
    public void Collect_InvalidRequiredManifest_Fails(string content)
    {
        var input = content == "missing" ? Path.Combine(_directory, "missing.json") : FileWith("bad.json", content);
        Attach(new CollectModuleManifests { ModuleManifestFiles = [new TaskItem(input)], OutputPath = Path.Combine(_directory, "out.json") }).Execute().Should().BeFalse();
    }

    [Fact]
    public void Collect_Empty_ReplacesStaleManifest()
    {
        var output = FileWith("out.json", "stale");
        Attach(new CollectModuleManifests { OutputPath = output }).Execute().Should().BeTrue();
        File.ReadAllText(output).Trim().Should().Be("[]");
    }

    [Fact]
    public void Collect_EvaluatedReferences_DeduplicatesIdenticalMetadata()
    {
        var local = FileWith("local.json", JsonSerializer.Serialize(new[] { Module("Ns.A", 1, "Ns.B", "Ns.C") }));
        var referenced = FileWith("evaluated-custom-Release.json", JsonSerializer.Serialize(new[] { Module("Ns.A", 1, "Ns.C", "Ns.B"), Module("Ns.B"), Module("Ns.C") }));
        var output = Path.Combine(_directory, "out.json");
        var task = Attach(new CollectModuleManifests
        {
            ModuleManifestFiles = [new TaskItem(local)], ReferencedModuleManifestFiles = [new TaskItem(referenced)], OutputPath = output
        });
        task.Execute().Should().BeTrue();
        var code = File.ReadAllText(output);
        JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(code)!.Select(m => m.FullName).Should().Equal("Ns.A", "Ns.B", "Ns.C");
        task.ModuleManifestFiles = [new TaskItem(referenced)];
        task.ReferencedModuleManifestFiles = [new TaskItem(local)];
        task.Execute().Should().BeTrue();
        File.ReadAllText(output).Should().Be(code);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("{")]
    public void Collect_InvalidReferencedInput_Fails(string content)
    {
        var path = content == "missing" ? Path.Combine(_directory, "missing.json") : FileWith("invalid.json", content);
        Attach(new CollectModuleManifests { ReferencedModuleManifestFiles = [new TaskItem(path)], OutputPath = Path.Combine(_directory, "out.json") }).Execute().Should().BeFalse();
    }

    [Fact]
    public void Collect_DuplicateWithinOneManifest_Fails()
    {
        var path = FileWith("duplicate.json", JsonSerializer.Serialize(new[] { Module("Ns.A"), Module("Ns.A") }));
        Attach(new CollectModuleManifests { ModuleManifestFiles = [new TaskItem(path)], OutputPath = Path.Combine(_directory, "out.json") }).Execute().Should().BeFalse();
    }

    [Fact]
    public void Collect_ConflictingIdentity_Fails()
    {
        Attach(new CollectModuleManifests
        {
            ModuleManifestFiles = [new TaskItem(FileWith("one.json", JsonSerializer.Serialize(new[] { Module("Ns.A") })))],
            ReferencedModuleManifestFiles = [new TaskItem(FileWith("two.json", JsonSerializer.Serialize(new[] { Module("Ns.A", 1) })))],
            OutputPath = Path.Combine(_directory, "out.json")
        }).Execute().Should().BeFalse();
    }

    [Fact]
    public void Collect_LegacyProjectPaths_FailsInsteadOfGuessing()
    {
        Attach(new CollectModuleManifests { ReferencedProjectPaths = [new TaskItem("project.csproj")], OutputPath = Path.Combine(_directory, "out.json") }).Execute().Should().BeFalse();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[{}]")]
    public void Generate_InvalidManifest_Fails(string content)
    {
        Attach(new GenerateAggregatedModuleCode
        {
            ModuleManifestFile = content == "missing" ? Path.Combine(_directory, "missing.json") : FileWith("bad.json", content),
            OutputPath = Path.Combine(_directory, "initializer.cs"), Namespace = "Generated"
        }).Execute().Should().BeFalse();
    }

    [Fact]
    public void Scan_NestedNamespacesTypesAndDependencyArray_PreservesIdentity()
    {
        var source = FileWith("Nested.cs", """
            namespace Outer { namespace Inner {
              class Container {
                [CrestModule(new System.Type[] { typeof(global::Outer.Inner.Dep) })] public class Nested {}
              }
            } }
            """);
        var output = Path.Combine(_directory, "out.json");
        Attach(new ScanModulesFromSource { SourceFiles = source, OutputPath = output }).Execute().Should().BeTrue();
        var module = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!.Single();
        module.FullName.Should().Be("Outer.Inner.Container.Nested");
        module.Namespace.Should().Be("Outer.Inner");
        module.DependsOn.Should().Equal("Outer.Inner.Dep");
    }

    private static CollectModuleManifests.ModuleManifest Module(string fullName, int order = 0, params string[] dependencies) => new()
    {
        FullName = fullName, Name = fullName.Split('.').Last(), Namespace = fullName[..fullName.LastIndexOf('.')],
        Order = order, AutoRegisterServices = true, DependsOn = dependencies.ToList()
    };

    [Theory]
    [InlineData("cycle")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("ambiguous")]
    public void Generate_InvalidGraph_Fails(string kind)
    {
        var modules = kind switch
        {
            "cycle" => new[] { Module("Ns.A", 0, "B"), Module("Ns.B", 0, "A") },
            "unknown" => [Module("Ns.A", 0, "Missing")],
            "duplicate" => [Module("Ns.A"), Module("Ns.A")],
            _ => [Module("Ns.A", 0, "B"), Module("One.B"), Module("Two.B")]
        };
        Generate(modules).Should().BeFalse();
    }

    private bool Generate(IEnumerable<CollectModuleManifests.ModuleManifest> modules) => Attach(new GenerateAggregatedModuleCode
    {
        ModuleManifestFile = FileWith("aggregate.json", JsonSerializer.Serialize(modules)),
        OutputPath = Path.Combine(_directory, "initializer.cs"), Namespace = "Generated"
    }).Execute();

    [Fact]
    public void Generate_Empty_ReplacesStaleInitializerWithSameShape()
    {
        FileWith("initializer.cs", "Old.Module");
        Generate([]).Should().BeTrue();
        var code = File.ReadAllText(Path.Combine(_directory, "initializer.cs"));
        code.Should().Contain("RegisterAllModules").And.Contain("InitializeModulesAsync").And.NotContain("Old.Module").And.NotContain("services.AddSingleton<");
    }

    [Fact]
    public void Generate_DependencyBeforeDependent_StableOrderAndFullName()
    {
        var modules = new[] { Module("Ns.Z", -10, "Ns.Dep"), Module("Ns.B"), Module("Ns.Dep", 5), Module("Ns.A") };
        Generate(modules).Should().BeTrue();
        var code = File.ReadAllText(Path.Combine(_directory, "initializer.cs"));
        Generate(modules.Reverse()).Should().BeTrue();
        File.ReadAllText(Path.Combine(_directory, "initializer.cs")).Should().Be(code);
        code.IndexOf("services.AddSingleton<Ns.Dep>", StringComparison.Ordinal).Should().BeLessThan(code.IndexOf("services.AddSingleton<Ns.Z>", StringComparison.Ordinal));
        code.IndexOf("services.AddSingleton<Ns.A>", StringComparison.Ordinal).Should().BeLessThan(code.IndexOf("services.AddSingleton<Ns.B>", StringComparison.Ordinal));
        code.Should().NotContain("_registeredModules.OrderBy");
    }
}
