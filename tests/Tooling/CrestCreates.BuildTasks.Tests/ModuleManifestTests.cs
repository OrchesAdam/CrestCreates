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
        if (task is ScanModulesFromSource scanner)
            scanner.ReferencePaths =
            [
                new TaskItem(typeof(object).Assembly.Location),
                new TaskItem(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
                new TaskItem(typeof(CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute).Assembly.Location)
            ];
        return task;
    }

    [Fact]
    public void Scan_AllDeclarationsAndAttributeForms_IgnoresCommentsAndStrings()
    {
        var source = FileWith("Modules.cs", """
            using CrestCreates.Domain.Shared.Attributes;
            using CM = CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;
            namespace Ns;
            // [CrestModule] class Fake : ModuleBase {}
            class Parent : ModuleBase { string fake = "[CrestModule] class Fake : ModuleBase"; }
            [CM(typeof(global::Ns.Dep), Order = -2, AutoRegisterServices = false)]
            class First : Parent {}
            [global::CrestCreates.Domain.Shared.Attributes.CrestModule(DependsOn = new[] { typeof(Ns.First) })]
            class Second : Parent {}
            [CrestCreates.Domain.Shared.Attributes.CrestModule] class Dep : Parent {}
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
    [InlineData("TRACE;KEEP", true)]
    [InlineData("TRACE,KEEP", true)]
    [InlineData("TRACE,KEEP;EXTRA", true)]
    [InlineData("", false)]
    public void Scan_UsesEvaluatedPreprocessorSymbols(string defineConstants, bool accepted)
    {
        var source = FileWith("Conditional.cs", """
            using CrestCreates.Domain.Shared.Attributes;
            #if !KEEP
            #error Scanner must receive KEEP
            #endif
            [CrestModule] class ConditionalModule {}
            """);
        var output = FileWith("conditional-manifest.json", "stale");
        var task = Attach(new ScanModulesFromSource
        {
            SourceFiles = source,
            DefineConstants = defineConstants,
            OutputPath = output
        });

        task.Execute().Should().Be(accepted);
        if (accepted)
        {
            var modules = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!;
            modules.Select(m => m.FullName).Should().Equal("ConditionalModule");
        }
        else
        {
            _engine.Verify(e => e.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()), Times.AtLeastOnce);
        }
    }

    [Fact]
    public void Scan_PseudoCrestModuleAttributes_AreIgnored()
    {
        var source = FileWith("Pseudo.cs", """
            using System;
            namespace Other { public sealed class CrestModuleAttribute : Attribute {} }
            [Other.CrestModule] public class QualifiedFake {}
            namespace Other { [CrestModule] public class UnqualifiedFake {} }
            """);
        var output = FileWith("pseudo-manifest.json", "stale");

        Attach(new ScanModulesFromSource { SourceFiles = source, OutputPath = output })
            .Execute().Should().BeTrue();

        JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!
            .Should().BeEmpty();
    }

    [Fact]
    public void Scan_GlobalUsingAliasAcrossFiles_IsDiscovered()
    {
        var alias = FileWith("GlobalAlias.cs",
            "global using CrestModule = global::CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;\n");
        var module = FileWith("GlobalAliasModule.cs", "[CrestModule] public class GlobalAliasModule {}\n");
        var output = FileWith("global-alias-manifest.json", "stale");

        Attach(new ScanModulesFromSource
        {
            SourceFiles = alias + ";" + module,
            OutputPath = output
        }).Execute().Should().BeTrue();

        JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!
            .Select(m => m.FullName).Should().Equal("GlobalAliasModule");
    }

    [Fact]
    public void Scan_LocalShadowingAttribute_IsIgnoredDespiteFormalUsing()
    {
        var source = FileWith("Shadow.cs", """
            using CrestCreates.Domain.Shared.Attributes;
            namespace Other
            {
                public sealed class CrestModuleAttribute : System.Attribute {}
                [CrestModule] public class Ordinary {}
            }
            """);
        var output = FileWith("shadow-manifest.json", "stale");

        Attach(new ScanModulesFromSource { SourceFiles = source, OutputPath = output })
            .Execute().Should().BeTrue();

        JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("namespace CrestCreates.Domain.Shared.Attributes { [CrestModule] public class RealModule {} }", "CrestCreates.Domain.Shared.Attributes.RealModule")]
    [InlineData("namespace CrestCreates.Domain.Shared.Attributes; [CrestModule] public class RealModule {}", "CrestCreates.Domain.Shared.Attributes.RealModule")]
    [InlineData("namespace CrestCreates.Domain.Shared.Attributes.Child { [CrestModule] public class RealModule {} }", "CrestCreates.Domain.Shared.Attributes.Child.RealModule")]
    [InlineData("using CrestCreates.Domain.Shared.Attributes; namespace Outer.Inner { [CrestModule] public class Ordinary {} }", "")]
    [InlineData("using CrestCreates.Domain.Shared.Attributes; namespace Outer.Inner; [CrestModule] public class Ordinary {}", "")]
    [InlineData("using CrestCreates = Other; namespace Other { public class Ordinary {} } [global::CrestCreates.Domain.Shared.Attributes.CrestModule] public class RealModule {}", "RealModule")]
    [InlineData("global using CrestCreates = Other; namespace Other { public class Ordinary {} } [global::CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute] public class RealModule {}", "RealModule")]
    public void Scan_NamespaceLookupAndGlobalQualification_MatchesCompiledAttribute(string source, string expectedFullName)
    {
        var contracts = FileWith("AttributeContracts.cs", """
            namespace CrestCreates.Domain.Shared.Attributes { public class CrestModuleAttribute : System.Attribute {} }
            namespace Outer { public class CrestModuleAttribute : System.Attribute {} }
            """);
        var moduleSource = FileWith("NamespaceModule.cs", source);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "NamespaceModules",
            new[] { contracts, moduleSource }.Select(path => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(path))),
            new[] { Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Should().BeEmpty();
        var output = FileWith("namespace-manifest.json", "stale");

        Attach(new ScanModulesFromSource { SourceFiles = contracts + ";" + moduleSource, OutputPath = output })
            .Execute().Should().BeTrue();

        var modules = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!;
        modules.Select(m => m.FullName).Should().Equal(expectedFullName.Length == 0 ? Array.Empty<string>() : new[] { expectedFullName });
    }

    [Theory]
    [InlineData("nearest-using", true)]
    [InlineData("nearest-using-file-scoped", true)]
    [InlineData("enclosing-using", true)]
    [InlineData("formal-before-global", true)]
    [InlineData("nested-shadow", false)]
    [InlineData("nested-shadow-with-using", false)]
    [InlineData("unrelated-container", true)]
    public void Scan_ScopePrecedence_MatchesCompilerBinding(string scenario, bool isFormal)
    {
        const string formalName = "CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute";
        var source = scenario switch
        {
            "nearest-using" => """
                namespace Outer { public class CrestModuleAttribute : System.Attribute {} }
                namespace Outer.Inner {
                    using CrestCreates.Domain.Shared.Attributes;
                    [CrestModule] public class RealModule {}
                }
                """,
            "nearest-using-file-scoped" => """
                namespace Outer.Inner;
                using CrestCreates.Domain.Shared.Attributes;
                [CrestModule] public class RealModule {}
                """,
            "enclosing-using" => """
                public class CrestModuleAttribute : System.Attribute {}
                namespace Outer {
                    using CrestCreates.Domain.Shared.Attributes;
                    namespace Inner { [CrestModule] public class RealModule {} }
                }
                """,
            "nested-shadow-with-using" => """
                using CrestCreates.Domain.Shared.Attributes;
                namespace CrestCreates.Domain.Shared.Attributes {
                    public class Container {
                        public class CrestModuleAttribute : System.Attribute {}
                        [CrestModule] public class Ordinary {}
                    }
                }
                """,
            "unrelated-container" => """
                namespace Outer {
                    public class Container { public class CrestModuleAttribute : System.Attribute {} }
                }
                namespace Outer.Inner {
                    using CrestCreates.Domain.Shared.Attributes;
                    public class Container { [CrestModule] public class RealModule {} }
                }
                """,
            "formal-before-global" => """
                public class CrestModuleAttribute : System.Attribute {}
                namespace CrestCreates.Domain.Shared.Attributes { [CrestModule] public class RealModule {} }
                """,
            _ => """
                namespace CrestCreates.Domain.Shared.Attributes {
                    public class Container {
                        public class CrestModuleAttribute : System.Attribute {}
                        [CrestModule] public class Ordinary {}
                    }
                }
                """
        };
        var contracts = FileWith("ScopeContracts.cs", "namespace CrestCreates.Domain.Shared.Attributes { public class CrestModuleAttribute : System.Attribute {} }"
            + (scenario == "nearest-using-file-scoped" ? "\nnamespace Outer { public class CrestModuleAttribute : System.Attribute {} }" : ""));
        var input = FileWith("ScopeModule.cs", source);
        var trees = new[] { contracts, input }.Select(path => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(path))).ToArray();
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("ScopeModules", trees,
            new[] { Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
        var attribute = trees[1].GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax>().Single();
        var boundName = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetTypeInfo(compilation.GetSemanticModel(trees[1]), attribute).Type!.ToDisplayString();
        (boundName == formalName).Should().Be(isFormal);
        var output = FileWith("scope-manifest.json", "stale");

        Attach(new ScanModulesFromSource { SourceFiles = contracts + ";" + input, OutputPath = output }).Execute().Should().BeTrue();

        var modules = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!;
        modules.Count.Should().Be(isFormal ? 1 : 0, "compiler bound the attribute to {0}", boundName);
    }

    [Theory]
    [InlineData("relative-qualified")]
    [InlineData("relative-using")]
    [InlineData("inherited-nested")]
    public void Scan_RelativeAndInheritedAttributeBindings_ExcludeOtherTypes(string scenario)
    {
        const string formalName = "CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute";
        var source = scenario switch
        {
            "relative-qualified" => """
                namespace Outer {
                    namespace CrestCreates.Domain.Shared.Attributes { public class CrestModuleAttribute : System.Attribute {} }
                    [CrestCreates.Domain.Shared.Attributes.CrestModule] public class Ordinary {}
                }
                """,
            "relative-using" => """
                namespace Outer {
                    using CrestCreates.Domain.Shared.Attributes;
                    namespace CrestCreates.Domain.Shared.Attributes { public class CrestModuleAttribute : System.Attribute {} }
                    [CrestModule] public class Ordinary {}
                }
                """,
            _ => """
                using CrestCreates.Domain.Shared.Attributes;
                public class Parent { public class CrestModuleAttribute : System.Attribute {} }
                public class Derived : Parent { [CrestModule] public class Ordinary {} }
                """
        };
        var contracts = FileWith("BindingContracts.cs", "namespace CrestCreates.Domain.Shared.Attributes { public class CrestModuleAttribute : System.Attribute {} }");
        var input = FileWith("BindingModule.cs", source);
        var trees = new[] { contracts, input }.Select(path => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(path))).ToArray();
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("BindingModules", trees,
            new[] { Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
        var attribute = trees[1].GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax>().Single();
        var boundName = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetTypeInfo(compilation.GetSemanticModel(trees[1]), attribute).Type!.ToDisplayString();
        boundName.Should().NotBe(formalName);
        var output = FileWith("binding-manifest.json", "stale");

        Attach(new ScanModulesFromSource { SourceFiles = contracts + ";" + input, OutputPath = output }).Execute().Should().BeTrue();

        var modules = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!;
        modules.Should().BeEmpty("compiler bound the attribute to {0}", boundName);
    }

    [Theory]
    [InlineData("contracts")]
    [InlineData("global,contracts")]
    [InlineData("global, contracts,extra")]
    public void Scan_ExternAliasReference_DiscoversFormalModule(string aliases)
    {
        var source = """
            extern alias contracts;
            using CM = contracts::CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;
            [CM] public class RealModule {}
            """;
        var input = FileWith("ExternAlias.cs", source);
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var reference = new TaskItem(typeof(CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute).Assembly.Location);
        reference.SetMetadata("Aliases", aliases);
        var task = Attach(new ScanModulesFromSource { SourceFiles = input, OutputPath = Path.Combine(_directory, "extern.json") });
        task.ReferencePaths = [task.ReferencePaths[0], task.ReferencePaths[1], reference];
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("ExternAliasModules", [tree],
            task.ReferencePaths.Select(item => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(item.ItemSpec,
                item == reference
                    ? Microsoft.CodeAnalysis.MetadataReferenceProperties.Assembly.WithAliases(aliases.Split(',').Select(alias => alias.Trim()))
                    : Microsoft.CodeAnalysis.MetadataReferenceProperties.Assembly)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
        Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetTypeInfo(compilation.GetSemanticModel(tree), tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax>().Single())
            .Type!.ToDisplayString().Should().Be("CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute");

        task.Execute().Should().BeTrue();

        JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(task.OutputPath))!
            .Select(m => m.FullName).Should().Equal("RealModule");
    }

    [Theory]
    [InlineData("Dep")]
    [InlineData("DepAlias")]
    [InlineData("global::One.Dep")]
    public void Scan_Dependency_UsesBoundIdentity(string dependency)
    {
        var source = $$"""
            using CrestCreates.Domain.Shared.Attributes;
            using DepAlias = One.Dep;
            namespace One {
                [CrestModule] public class Dep {}
                [CrestModule(typeof({{dependency}}))] public class Root {}
            }
            namespace Two { [CrestModule] public class Dep {} }
            """;
        var task = Attach(new ScanModulesFromSource
        {
            SourceFiles = FileWith("ScopedDeps.cs", source),
            OutputPath = Path.Combine(_directory, "deps.json")
        });
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("DependencyModules", [tree],
            task.ReferencePaths.Select(item => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(item.ItemSpec)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
        Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetTypeInfo(compilation.GetSemanticModel(tree), tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeOfExpressionSyntax>().Single().Type)
            .Type!.ToDisplayString().Should().Be("One.Dep");

        task.Execute().Should().BeTrue();

        var modules = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(task.OutputPath))!;
        modules.Single(module => module.FullName == "One.Root").DependsOn.Should().Equal("One.Dep");
        Generate(modules).Should().BeTrue("the compiler binds the dependency without ambiguity");
    }

    [Theory]
    [InlineData("Priorities.Core", "Flags.Enabled", -10, false)]
    [InlineData("0", "Flags.Enabled", 0, false)]
    [InlineData("1_000", "!Flags.Enabled", 1000, true)]
    [InlineData("0x10", "true && !Flags.Enabled", 16, true)]
    [InlineData("(byte)7", "false", 7, false)]
    [InlineData("'A'", "true", 65, true)]
    public void Scan_ConstantArguments_UsesCompilerValues(string order, string autoRegister, int expectedOrder, bool expectedAutoRegister)
    {
        var source = $$"""
            using CrestCreates.Domain.Shared.Attributes;
            public static class Priorities { public const int Core = -10; }
            public static class Flags { public const bool Enabled = false; }
            [CrestModule(Order = {{order}}, AutoRegisterServices = {{autoRegister}})] public class RealModule {}
            """;
        var task = Attach(new ScanModulesFromSource { SourceFiles = FileWith("Constants.cs", source), OutputPath = Path.Combine(_directory, "constants.json") });
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("ConstantModules", [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)],
            task.ReferencePaths.Select(item => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(item.ItemSpec)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();

        task.Execute().Should().BeTrue();

        var module = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(task.OutputPath))!.Single();
        module.Order.Should().Be(expectedOrder);
        module.AutoRegisterServices.Should().Be(expectedAutoRegister);
    }

    [Theory]
    [InlineData("@One", "Dep", "One", "One.Dep")]
    [InlineData("One . Nested", "Dep", "One.Nested", "One.Nested.Dep")]
    [InlineData("@event", "Dep", "@event", "@event.Dep")]
    [InlineData("One", "@class", "One", "One.@class")]
    [InlineData("One", "@Dep", "One", "One.Dep")]
    public void Scan_DeclarationAndDependency_UseCanonicalIdentity(string namespaceText, string typeName, string expectedNamespace, string expectedFullName)
    {
        var source = $$"""
            using CrestCreates.Domain.Shared.Attributes;
            namespace {{namespaceText}} {
                [CrestModule] public class {{typeName}} {}
                [CrestModule(typeof({{typeName}}))] public class Root {}
            }
            """;
        var task = Attach(new ScanModulesFromSource { SourceFiles = FileWith("Identities.cs", source), OutputPath = Path.Combine(_directory, "identities.json") });
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("CanonicalModules", [tree],
            task.ReferencePaths.Select(item => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(item.ItemSpec)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
        Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetTypeInfo(compilation.GetSemanticModel(tree), tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeOfExpressionSyntax>().Single().Type)
            .Type!.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.CSharpErrorMessageFormat).Should().Be(expectedFullName);

        task.Execute().Should().BeTrue();

        var modules = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(task.OutputPath))!;
        var dependency = modules.Single(module => module.Name == typeName.TrimStart('@'));
        dependency.Namespace.Should().Be(expectedNamespace);
        dependency.FullName.Should().Be(expectedFullName);
        modules.Single(module => module.Name == "Root").DependsOn.Should().Equal(expectedFullName);
        Generate(modules).Should().BeTrue();
        File.ReadAllText(Path.Combine(_directory, "initializer.cs")).Should().Contain($"services.AddSingleton<{expectedFullName}>();");
    }

    [Theory]
    [InlineData("no-references")]
    [InlineData("missing-contract")]
    [InlineData("alias-mismatch")]
    [InlineData("ambiguous-contract")]
    public void Scan_UnboundAttribute_FailsWithLocationAndPreservesManifest(string scenario)
    {
        var source = scenario == "alias-mismatch"
            ? "extern alias contracts;\nusing CM = contracts::CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;\n[CM] public class RealModule {}"
            : "using CrestCreates.Domain.Shared.Attributes;\n[CrestModule] public class RealModule {}";
        var input = FileWith("Unbound.cs", source);
        var output = FileWith("unbound-manifest.json", "stale");
        var task = Attach(new ScanModulesFromSource { SourceFiles = input, OutputPath = output });
        if (scenario == "no-references") task.ReferencePaths = [];
        if (scenario == "missing-contract") task.ReferencePaths = task.ReferencePaths.Take(2).ToArray();
        if (scenario == "ambiguous-contract")
        {
            var duplicateSource = "namespace CrestCreates.Domain.Shared.Attributes { public class CrestModuleAttribute : System.Attribute {} }";
            var duplicate = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("ConflictingModuleContract",
                [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(duplicateSource)],
                task.ReferencePaths.Take(2).Select(reference => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(reference.ItemSpec)),
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
            var assembly = Path.Combine(_directory, "ConflictingModuleContract.dll");
            using (var stream = File.Create(assembly)) duplicate.Emit(stream).Success.Should().BeTrue();
            task.ReferencePaths = [.. task.ReferencePaths, new TaskItem(assembly)];
        }
        var errors = new List<BuildErrorEventArgs>();
        _engine.Setup(engine => engine.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()))
            .Callback<BuildErrorEventArgs>(errors.Add);

        task.Execute().Should().BeFalse();

        var error = errors.Should().ContainSingle().Which;
        error.Code.Should().Be("CCMB001");
        error.File.Should().Be(input);
        error.LineNumber.Should().Be(scenario == "alias-mismatch" ? 3 : 2);
        error.Message.Should().NotBeNullOrWhiteSpace();
        File.ReadAllText(output).Should().Be("stale");
    }

    [Fact]
    public void Scan_NoReferencesOrAttributes_WritesEmptyManifest()
    {
        var output = FileWith("plain-manifest.json", "stale");
        var task = Attach(new ScanModulesFromSource { SourceFiles = FileWith("Plain.cs", "public class Plain {}"), OutputPath = output });
        task.ReferencePaths = [];

        task.Execute().Should().BeTrue();

        File.ReadAllText(output).Trim().Should().Be("[]");
        _engine.Verify(engine => engine.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scan_ResolvedOtherAttribute_WritesEmptyWithoutError(bool includeFormalReference)
    {
        var source = FileWith("ResolvedOther.cs", """
            namespace Other { public class CrestModuleAttribute : System.Attribute {} }
            [Other.CrestModule] public class Ordinary {}
            """);
        var output = FileWith("other-manifest.json", "stale");
        var task = Attach(new ScanModulesFromSource { SourceFiles = source, OutputPath = output });
        if (!includeFormalReference) task.ReferencePaths = task.ReferencePaths.Take(2).ToArray();

        task.Execute().Should().BeTrue();

        File.ReadAllText(output).Trim().Should().Be("[]");
        _engine.Verify(engine => engine.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scan_GeneratorSuppliedAttribute_SucceedsBeforeGeneratedInput(bool includeGeneratedInput)
    {
        const string source = "[Generated.Marker] public class Ordinary {}";
        var input = FileWith("GeneratedConsumer.cs", source);
        var output = FileWith("generated-manifest.json", "stale");
        var task = Attach(new ScanModulesFromSource { SourceFiles = input, OutputPath = output });
        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("GeneratedConsumer",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, parseOptions)],
            task.ReferencePaths.Select(reference => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(reference.ItemSpec)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        var driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(
                [Microsoft.CodeAnalysis.GeneratorExtensions.AsSourceGenerator(new GeneratedMarkerProvider())], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generatedCompilation, out var diagnostics);
        diagnostics.Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
        using var artifact = new MemoryStream();
        var emitted = generatedCompilation.Emit(artifact);
        emitted.Success.Should().BeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));
        if (includeGeneratedInput)
            task.SourceFiles += ";" + FileWith("Marker.g.cs", driver.GetRunResult().GeneratedTrees.Single().GetText().ToString());

        task.Execute().Should().BeTrue("the actual generator run and final C# emit succeed with the same resolved references");

        JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!.Should().BeEmpty();
        _engine.Verify(engine => engine.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()), Times.Never);
    }

    private sealed class GeneratedMarkerProvider : Microsoft.CodeAnalysis.IIncrementalGenerator
    {
        public void Initialize(Microsoft.CodeAnalysis.IncrementalGeneratorInitializationContext context)
            => context.RegisterPostInitializationOutput(initialization => initialization.AddSource("Marker.g.cs", """
                namespace Generated {
                    [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
                    internal sealed class MarkerAttribute : global::System.Attribute {}
                }
                """));
    }

    [Theory]
    [InlineData("other-file", false)]
    [InlineData("other-file", true)]
    [InlineData("outer-namespace", false)]
    [InlineData("outer-namespace", true)]
    [InlineData("nested-namespace", false)]
    [InlineData("nested-namespace", true)]
    [InlineData("global-using", false)]
    [InlineData("global-using", true)]
    [InlineData("namespace-alias-shadow", false)]
    [InlineData("namespace-alias-shadow", true)]
    [InlineData("namespace-alias-reverse", false)]
    [InlineData("namespace-alias-reverse", true)]
    public void Scan_GeneratedAttributeAlias_UsesItsVisibleScope(string scenario, bool includeGeneratedInput)
    {
        const string formal = "using CM = CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;";
        const string generated = "using CM = Generated.MarkerAttribute;";
        var sources = scenario switch
        {
            "other-file" => new[] { formal + "\n[CM] public class RealModule {}", generated + "\n[CM] public class Ordinary {}" },
            "outer-namespace" => [formal + "\nnamespace Inner {\n" + generated + "\n[CM] public class Ordinary {}\n}" ],
            "nested-namespace" => [formal + "\nnamespace Outer {\n[CM] public class RealModule {}\nnamespace Inner {\n" + generated + "\n[CM] public class Ordinary {}\n}\n}" ],
            "namespace-alias-shadow" => ["namespace Outer {\n" + formal + "\nnamespace Inner {\n" + generated + "\n[CM] public class Ordinary {}\n}\n}"],
            "namespace-alias-reverse" => ["namespace Outer {\n" + generated + "\n[CM] public class Ordinary {}\nnamespace Inner {\n" + formal + "\n[CM] public class RealModule {}\n}\n}"],
            _ => ["global " + formal, "namespace Consumer {\n" + generated + "\n[CM] public class Ordinary {}\n}"]
        };
        var inputs = sources.Select((source, index) => FileWith($"ScopedAlias{index}.cs", source)).ToArray();
        var output = FileWith("scoped-alias-manifest.json", "stale");
        var task = Attach(new ScanModulesFromSource { SourceFiles = string.Join(';', inputs), OutputPath = output });
        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("ScopedGeneratedAliases",
            sources.Select(source => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, parseOptions)),
            task.ReferencePaths.Select(reference => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(reference.ItemSpec)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        var driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(
                [Microsoft.CodeAnalysis.GeneratorExtensions.AsSourceGenerator(new GeneratedMarkerProvider())], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generatedCompilation, out var diagnostics);
        diagnostics.Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
        using var artifact = new MemoryStream();
        var emitted = generatedCompilation.Emit(artifact);
        emitted.Success.Should().BeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));
        if (includeGeneratedInput)
            task.SourceFiles += ";" + FileWith("ScopedMarker.g.cs", driver.GetRunResult().GeneratedTrees.Single().GetText().ToString());

        task.Execute().Should().BeTrue("the actual generator run and final emit bind each alias in its own visible scope");

        JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!
            .Select(module => module.FullName).Should().Equal(scenario switch
        {
            "other-file" => new[] { "RealModule" },
            "nested-namespace" => new[] { "Outer.RealModule" },
            "namespace-alias-reverse" => new[] { "Outer.Inner.RealModule" },
            _ => Array.Empty<string>()
        });
        _engine.Verify(engine => engine.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()), Times.Never);
    }

    [Theory]
    [InlineData("CM", false)]
    [InlineData("CM", true)]
    [InlineData("CMAttribute", false)]
    [InlineData("CMAttribute", true)]
    [InlineData("@CM", false)]
    [InlineData("@CM", true)]
    [InlineData("@CMAttribute", false)]
    [InlineData("@CMAttribute", true)]
    public void Scan_AttributeSuffixAlias_MatchesCompiler(string attributeName, bool missingContract)
    {
        var source = "using CMAttribute = CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;\n[" + attributeName + "] public class RealModule {}";
        var input = FileWith("SuffixAlias.cs", source);
        var output = FileWith("suffix-alias.json", "stale");
        var task = Attach(new ScanModulesFromSource { SourceFiles = input, OutputPath = output });
        if (missingContract) task.ReferencePaths = task.ReferencePaths.Take(2).ToArray();
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("SuffixAlias",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)],
            task.ReferencePaths.Select(reference => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(reference.ItemSpec)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var artifact = new MemoryStream();
        var emitted = compilation.Emit(artifact);
        var formalCandidate = attributeName != "@CM";
        emitted.Success.Should().Be(formalCandidate && !missingContract, string.Join(Environment.NewLine, emitted.Diagnostics));
        var errors = new List<BuildErrorEventArgs>();
        _engine.Setup(engine => engine.LogErrorEvent(It.IsAny<BuildErrorEventArgs>())).Callback<BuildErrorEventArgs>(errors.Add);

        task.Execute().Should().Be(!(formalCandidate && missingContract));

        if (formalCandidate && missingContract)
        {
            var error = errors.Should().ContainSingle().Which;
            error.Code.Should().Be("CCMB001");
            error.File.Should().Be(input);
            error.LineNumber.Should().Be(2);
            File.ReadAllText(output).Should().Be("stale");
        }
        else
        {
            errors.Should().BeEmpty();
            JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!
                .Select(module => module.FullName).Should().Equal(formalCandidate ? new[] { "RealModule" } : Array.Empty<string>());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Scan_UsingTarget_ResolvesEnclosingAliases(bool globalAlias, bool missingContract)
    {
        const string alias = "using Inner = CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;";
        const string consumer = "namespace Consumer { using Outer = Inner; [Outer] public class RealModule {} }";
        var sources = globalAlias ? new[] { "global " + alias, consumer } : new[] { alias + "\n" + consumer };
        var output = FileWith("enclosing-alias.json", "stale");
        var task = Attach(new ScanModulesFromSource
        {
            SourceFiles = string.Join(';', sources.Select((source, index) => FileWith($"EnclosingAlias{index}.cs", source))),
            OutputPath = output
        });
        if (missingContract) task.ReferencePaths = task.ReferencePaths.Take(2).ToArray();
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("EnclosingAlias",
            sources.Select(source => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)),
            task.ReferencePaths.Select(reference => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(reference.ItemSpec)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var artifact = new MemoryStream();
        var emitted = compilation.Emit(artifact);
        emitted.Success.Should().Be(!missingContract, string.Join(Environment.NewLine, emitted.Diagnostics));
        var errors = new List<BuildErrorEventArgs>();
        _engine.Setup(engine => engine.LogErrorEvent(It.IsAny<BuildErrorEventArgs>())).Callback<BuildErrorEventArgs>(errors.Add);

        task.Execute().Should().Be(!missingContract);

        if (missingContract)
        {
            errors.Should().ContainSingle().Which.Code.Should().Be("CCMB001");
            File.ReadAllText(output).Should().Be("stale");
        }
        else
        {
            errors.Should().BeEmpty();
            JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!
                .Select(module => module.FullName).Should().Equal("Consumer.RealModule");
        }
    }

    [Theory]
    [InlineData("root", false)]
    [InlineData("root", true)]
    [InlineData("namespace", false)]
    [InlineData("namespace", true)]
    [InlineData("global", false)]
    [InlineData("global", true)]
    public void Scan_UsingTarget_ExcludesPeerAliasesBeforeGenerator(string scope, bool includeGeneratedInput)
    {
        const string aliases = "using Marker = CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;\nusing CM = Marker;\n[CM] public class Ordinary {}";
        var source = scope switch
        {
            "namespace" => "namespace Consumer {\n" + aliases + "\n}",
            "global" => "global " + aliases,
            _ => aliases
        };
        var output = FileWith("using-target.json", "stale");
        var task = Attach(new ScanModulesFromSource { SourceFiles = FileWith("UsingTarget.cs", source), OutputPath = output });
        var options = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("UsingTarget",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, options)],
            task.ReferencePaths.Select(reference => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(reference.ItemSpec)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        var driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(
                [Microsoft.CodeAnalysis.GeneratorExtensions.AsSourceGenerator(new GeneratedAliasTargetProvider())], parseOptions: options)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generatedCompilation, out var diagnostics);
        diagnostics.Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
        using var artifact = new MemoryStream();
        var emitted = generatedCompilation.Emit(artifact);
        emitted.Success.Should().BeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));
        if (includeGeneratedInput)
            task.SourceFiles += ";" + FileWith("AliasTarget.g.cs", driver.GetRunResult().GeneratedTrees.Single().GetText().ToString());

        task.Execute().Should().BeTrue("using targets do not bind through aliases declared in the same scope");

        File.ReadAllText(output).Trim().Should().Be("[]");
        _engine.Verify(engine => engine.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()), Times.Never);
    }

    private sealed class GeneratedAliasTargetProvider : Microsoft.CodeAnalysis.IIncrementalGenerator
    {
        public void Initialize(Microsoft.CodeAnalysis.IncrementalGeneratorInitializationContext context)
            => context.RegisterPostInitializationOutput(initialization => initialization.AddSource("AliasTarget.g.cs", "public sealed class Marker : global::System.Attribute {}"));
    }

    [Theory]
    [InlineData("using Inner = CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;\nusing Outer = Inner;\n[Outer] public class Ordinary {}")]
    [InlineData("using A = B;\nusing B = A;\n[A] public class Ordinary {}")]
    public void Scan_InvalidPeerAliasChains_LeaveBindingToCoreCompile(string source)
    {
        var output = FileWith("invalid-alias.json", "stale");
        var task = Attach(new ScanModulesFromSource { SourceFiles = FileWith("InvalidAlias.cs", source), OutputPath = output });
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("InvalidAlias",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)],
            task.ReferencePaths.Select(reference => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(reference.ItemSpec)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Should().Contain(diagnostic => diagnostic.Id == "CS0246" && diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

        task.Execute().Should().BeTrue("peer aliases cannot establish a formal module attribute identity");

        File.ReadAllText(output).Trim().Should().Be("[]");
        _engine.Verify(engine => engine.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()), Times.Never);
    }

    [Fact]
    public void Scan_UnboundOrdinaryAttribute_LeavesBindingToCoreCompile()
    {
        const string source = "[MissingModule] public class Ordinary {}";
        var input = FileWith("OrdinaryUnbound.cs", source);
        var output = FileWith("ordinary-unbound.json", "stale");
        var task = Attach(new ScanModulesFromSource { SourceFiles = input, OutputPath = output });
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("OrdinaryUnbound",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)],
            task.ReferencePaths.Select(reference => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(reference.ItemSpec)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Should().Contain(diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && diagnostic.Id == "CS0246");

        task.Execute().Should().BeTrue("ordinary attribute binding is CoreCompile's responsibility, not the scanner's");

        File.ReadAllText(output).Trim().Should().Be("[]");
        _engine.Verify(engine => engine.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()), Times.Never);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("chain")]
    [InlineData("global-using")]
    [InlineData("namespace-alias")]
    [InlineData("namespace-chain")]
    [InlineData("nested-namespace")]
    public void Scan_UnboundModuleAttributeAlias_FailsClosed(string scenario)
    {
        var consumer = scenario switch
        {
            "direct" => "using CM = CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;\n[CM] public class RealModule {}",
            "chain" => "using Inner = CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;\nnamespace Consumer { using Outer = Inner;\n[Outer] public class RealModule {} }",
            "global-using" => "[CM] public class RealModule {}",
            "namespace-alias" => "using N = CrestCreates.Domain.Shared.Attributes;\n[N.CrestModule] public class RealModule {}",
            "namespace-chain" => "using N = CrestCreates.Domain.Shared.Attributes;\nusing CM = N.CrestModuleAttribute;\n[CM] public class RealModule {}",
            "nested-namespace" => """
                namespace Other { public class MarkerAttribute : System.Attribute {} }
                namespace Outer {
                    using CM = Other.MarkerAttribute;
                    namespace Inner {
                        using CM = CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;
                        [CM] public class RealModule {}
                    }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var input = FileWith("AliasUnbound.cs", consumer);
        if (scenario == "global-using")
            input = FileWith("GlobalUsings.cs", "global using CM = CrestCreates.Domain.Shared.Attributes.CrestModuleAttribute;") + ";" + input;
        var output = FileWith("alias-unbound.json", "stale");
        var task = Attach(new ScanModulesFromSource { SourceFiles = input, OutputPath = output });
        task.ReferencePaths = task.ReferencePaths.Take(2).ToArray();
        var errors = new List<BuildErrorEventArgs>();
        _engine.Setup(engine => engine.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()))
            .Callback<BuildErrorEventArgs>(errors.Add);

        task.Execute().Should().BeFalse("an unbound name that could denote the formal module attribute must not be skipped");

        var error = errors.Should().ContainSingle().Which;
        error.Code.Should().Be("CCMB001", error.Message);
        error.File.Should().Be(scenario == "global-using" ? input.Split(';')[1] : input);
        File.ReadAllText(output).Should().Be("stale");
    }

    [Fact]
    public void Scan_PartialModuleWithMixedAttributes_IsDiscoveredOnce()
    {
        var first = FileWith("PartialFirst.cs", "namespace Ns; public partial class RealModule {}");
        var second = FileWith("PartialSecond.cs", """
            using CrestCreates.Domain.Shared.Attributes;
            namespace Ns;
            [System.Obsolete]
            [CrestModule(Order = 3)]
            public partial class RealModule {}
            """);
        var output = FileWith("partial-manifest.json", "stale");

        Attach(new ScanModulesFromSource { SourceFiles = first + ";" + second, OutputPath = output }).Execute().Should().BeTrue();

        var module = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!.Should().ContainSingle().Which;
        module.FullName.Should().Be("Ns.RealModule");
        module.Order.Should().Be(3);
        _engine.Verify(engine => engine.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()), Times.Never);
    }

    [Theory]
    [InlineData("[CrestModule(Order = wrong)] class M {}")]
    [InlineData("[CrestModule(AutoRegisterServices = 1)] class M {}")]
    [InlineData("[CrestModule(typeof(Missing))] class M {}")]
    [InlineData("[CrestModule] class M {")]
    [InlineData("[CrestModule] class M {} [CrestModule] class M {}")]
    public void Scan_InvalidDeclarations_Fail(string source)
    {
        Attach(new ScanModulesFromSource
        {
            SourceFiles = FileWith("Bad.cs", "using CrestCreates.Domain.Shared.Attributes;\n" + source),
            OutputPath = Path.Combine(_directory, "out.json")
        }).Execute().Should().BeFalse();
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
            using CrestCreates.Domain.Shared.Attributes;
            namespace Outer { namespace Inner {
              [CrestModule] class Dep {}
              class Container {
                [CrestModule(new System.Type[] { typeof(global::Outer.Inner.Dep) })] public class Nested {}
              }
            } }
            """);
        var output = Path.Combine(_directory, "out.json");
        Attach(new ScanModulesFromSource { SourceFiles = source, OutputPath = output }).Execute().Should().BeTrue();
        var module = JsonSerializer.Deserialize<List<CollectModuleManifests.ModuleManifest>>(File.ReadAllText(output))!
            .Single(module => module.Name == "Nested");
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
        code.Should().Contain("RegisterAllModules").And.Contain("InitializeModulesAsync")
            .And.NotContain("Old.Module").And.NotContain("services.AddSingleton<")
            .And.NotContain("IServiceCollection").And.NotContain("ModuleDiagnosticsStore");
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
