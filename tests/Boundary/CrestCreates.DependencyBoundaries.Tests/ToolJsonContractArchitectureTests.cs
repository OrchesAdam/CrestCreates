namespace CrestCreates.DependencyBoundaries.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

public sealed class ToolJsonContractArchitectureTests
{
    private const string AgentContributorInterface = "CrestCreates.Agent.Tools.IAgentToolJsonContextContributor";
    private const string McpContributorInterface = "CrestCreates.Mcp.Abstractions.IMcpToolJsonContextContributor";
    private const string JsonSerializerContext = "System.Text.Json.Serialization.JsonSerializerContext";
    private const string JsonContractSurfaceAttribute = "CrestCreates.Core.Abstractions.Serialization.JsonContractSurfaceAttribute";
    private const string JsonSerializableAttribute = "System.Text.Json.Serialization.JsonSerializableAttribute";
    private const string AgentToolSpecsAttribute = "CrestCreates.Agent.Tools.AgentToolSpecsAttribute";
    private const string McpToolSpecsAttribute = "CrestCreates.Mcp.McpToolSpecsAttribute";

    private static readonly ToolJsonContractOwnership[] s_ownershipLedger =
    [
        new(
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolJsonSerializerContext",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolJsonContextContributor",
            "CrestCreates.Agent.Memory.Tools.AgentMemoryToolJsonSerializerContext.AgentMemoryToolJsonSerializerContextRootManifest",
            ToolSurfaceAdapter.Agent),
        new(
            "CrestCreates.Mcp.Memory.Json.McpMemoryJsonSerializerContext",
            "CrestCreates.Mcp.Memory.Json.McpMemoryJsonContextContributor",
            "CrestCreates.Mcp.Memory.Json.McpMemoryJsonSerializerContext.McpMemoryJsonSerializerContextRootManifest",
            ToolSurfaceAdapter.Mcp),
    ];

    [Fact]
    public void RepositoryToolJsonContracts_FollowOwnershipLedger()
    {
        var analysis = AnalyzeRepository(FindRepoRoot());

        Assert.Equal(
            s_ownershipLedger.Select(item => item.ContributorMetadataName).Order(StringComparer.Ordinal),
            analysis.Contributors.Select(item => item.MetadataName).Order(StringComparer.Ordinal));
        Assert.Equal(
            s_ownershipLedger.Select(item => item.ContextMetadataName).Order(StringComparer.Ordinal),
            analysis.Contexts.Select(item => item.MetadataName).Order(StringComparer.Ordinal));

        foreach (var ownership in s_ownershipLedger)
        {
            var contributor = Assert.Single(
                analysis.Contributors,
                item => item.MetadataName == ownership.ContributorMetadataName);
            var context = Assert.Single(
                analysis.Contexts,
                item => item.MetadataName == ownership.ContextMetadataName);

            Assert.Equal(ownership.Adapter, context.Adapter);
            Assert.False(
                context.HasHandwrittenJsonSerializable,
                $"Tool Context '{context.MetadataName}' must derive binding roots from JsonContractSurface, not handwritten JsonSerializable attributes ({context.FilePath}).");
            Assert.True(
                ReturnsGeneratedBindingManifest(contributor.BindingRootProperty, ownership.ManifestMetadataName),
                $"Contributor '{contributor.MetadataName}' must directly return '{ownership.ManifestMetadataName}.BindingRootTypes' ({contributor.FilePath}).");
        }
    }

    [Fact]
    public void ContributorDiscovery_IsSemanticAcrossAliasesBaseClassesAndFormatting()
    {
        const string source = """
            using ContributorContract = global::CrestCreates.Agent.Tools.IAgentToolJsonContextContributor;
            namespace Sample;
            internal abstract class SomeBaseClass { }
            internal sealed class Contributor
                : SomeBaseClass,
                  ContributorContract
            {
                public System.Collections.Generic.IReadOnlyCollection<System.Type> BindingRootTypes
                    => GeneratedContext.GeneratedContextRootManifest.BindingRootTypes;
            }
            """;

        var contributor = Assert.Single(AnalyzeSources([("Contributor.cs", source)]).Contributors);

        Assert.Equal("Sample.Contributor", contributor.MetadataName);
        Assert.True(ReturnsGeneratedBindingManifest(
            contributor.BindingRootProperty,
            "Sample.GeneratedContext.GeneratedContextRootManifest"));
    }

    [Fact]
    public void ContributorGuard_AllowsUnrelatedTypeof_ButRejectsHandwrittenBindingRoots()
    {
        const string validSource = """
            namespace Sample;
            internal sealed class Valid : global::CrestCreates.Agent.Tools.IAgentToolJsonContextContributor
            {
                public System.Type DiagnosticType => typeof(string);
                public System.Collections.Generic.IReadOnlyCollection<System.Type> BindingRootTypes
                    => GeneratedContext.GeneratedContextRootManifest.BindingRootTypes;
            }
            """;
        const string invalidSource = """
            namespace Sample;
            internal sealed class Invalid : global::CrestCreates.Agent.Tools.IAgentToolJsonContextContributor
            {
                public System.Collections.Generic.IReadOnlyCollection<System.Type> BindingRootTypes
                    => new System.Type[] { typeof(string) };
            }
            """;

        var valid = Assert.Single(AnalyzeSources([("Valid.cs", validSource)]).Contributors);
        var invalid = Assert.Single(AnalyzeSources([("Invalid.cs", invalidSource)]).Contributors);

        Assert.True(ReturnsGeneratedBindingManifest(
            valid.BindingRootProperty,
            "Sample.GeneratedContext.GeneratedContextRootManifest"));
        Assert.False(ReturnsGeneratedBindingManifest(
            invalid.BindingRootProperty,
            "Sample.GeneratedContext.GeneratedContextRootManifest"));
    }

    [Fact]
    public void ContributorDiscovery_FollowsInterfaceAcrossProjectBaseClass()
    {
        const string baseProjectSource = """
            namespace Contracts;
            public abstract class AgentContributorBase
                : global::CrestCreates.Agent.Tools.IAgentToolJsonContextContributor
            {
            }
            """;
        const string derivedProjectSource = """
            namespace Feature;
            internal sealed class NewContributor : global::Contracts.AgentContributorBase
            {
                public System.Collections.Generic.IReadOnlyCollection<System.Type> BindingRootTypes
                    => new System.Type[] { typeof(string) };
            }
            """;

        var analysis = AnalyzeProductionProjects(
        [
            [("ProjectA/AgentContributorBase.cs", baseProjectSource)],
            [("ProjectB/NewContributor.cs", derivedProjectSource)],
        ]);
        var contributor = Assert.Single(analysis.Contributors);

        Assert.Equal("Feature.NewContributor", contributor.MetadataName);
        Assert.False(ReturnsGeneratedBindingManifest(
            contributor.BindingRootProperty,
            "Feature.GeneratedContext.GeneratedContextRootManifest"));
    }

    private static RepositoryAnalysis AnalyzeRepository(string root)
    {
        var sourceRoot = Path.Combine(root, "src");
        var productionProjects = Directory
            .EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(projectPath =>
            {
                var projectDirectory = Path.GetDirectoryName(projectPath)!;
                return Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                    .Where(path => IsProductionSourcePath(projectDirectory, path))
                    .Select(path => (Path: path, Text: File.ReadAllText(path)))
                    .ToArray();
            })
            .ToArray();

        return AnalyzeProductionProjects(productionProjects, includeGuardSymbols: false);
    }

    private static RepositoryAnalysis AnalyzeProductionProjects(
        IEnumerable<IEnumerable<(string Path, string Text)>> projects,
        bool includeGuardSymbols = true)
    {
        var productionSources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            foreach (var source in project)
                productionSources[source.Path] = source.Text;
        }

        var analysis = AnalyzeSources(
            productionSources.Select(source => (source.Key, source.Value)),
            includeGuardSymbols);
        analysis.Deduplicate();
        return analysis;
    }

    private static RepositoryAnalysis AnalyzeSources(
        IEnumerable<(string Path, string Text)> sources,
        bool includeGuardSymbols = true)
    {
        var sourceTrees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source.Text, path: source.Path))
            .ToArray();
        var globalUsings = CSharpSyntaxTree.ParseText(GuardGlobalUsings);
        var syntaxTrees = includeGuardSymbols
            ? new[] { globalUsings, CSharpSyntaxTree.ParseText(GuardSymbols) }.Concat(sourceTrees)
            : new[] { globalUsings }.Concat(sourceTrees);
        var compilation = CSharpCompilation.Create(
            $"ToolJsonContractGuard_{Guid.NewGuid():N}",
            syntaxTrees,
            PlatformReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var analysis = new RepositoryAnalysis();

        foreach (var tree in sourceTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type)
                    continue;

                if (IsConcreteContributor(type))
                {
                    var property = type.GetMembers("BindingRootTypes")
                        .OfType<IPropertySymbol>()
                        .SelectMany(member => member.DeclaringSyntaxReferences)
                        .Select(reference => reference.GetSyntax())
                        .OfType<PropertyDeclarationSyntax>()
                        .FirstOrDefault();

                    analysis.Contributors.Add(new ContributorContract(
                        GetMetadataName(type),
                        property,
                        tree.FilePath));
                }

                var adapter = GetToolSurfaceAdapter(type);
                if (adapter is null)
                    continue;

                analysis.Contexts.Add(new ToolContextContract(
                    GetMetadataName(type),
                    adapter.Value,
                    type.GetAttributes().Any(attribute => HasMetadataName(attribute.AttributeClass, JsonSerializableAttribute)),
                    tree.FilePath));
            }
        }

        return analysis;
    }

    private static bool IsConcreteContributor(INamedTypeSymbol type)
        => type.TypeKind == TypeKind.Class
            && !type.IsAbstract
            && type.AllInterfaces.Any(@interface =>
                HasMetadataName(@interface, AgentContributorInterface)
                || HasMetadataName(@interface, McpContributorInterface));

    private static ToolSurfaceAdapter? GetToolSurfaceAdapter(INamedTypeSymbol type)
    {
        if (!DerivesFrom(type, JsonSerializerContext))
            return null;

        var adapters = type.GetAttributes()
            .Where(attribute => HasMetadataName(attribute.AttributeClass, JsonContractSurfaceAttribute))
            .Select(attribute => attribute.ConstructorArguments.FirstOrDefault().Value)
            .OfType<INamedTypeSymbol>()
            .SelectMany(surface => surface.GetAttributes())
            .Select(attribute => HasMetadataName(attribute.AttributeClass, AgentToolSpecsAttribute)
                ? ToolSurfaceAdapter.Agent
                : HasMetadataName(attribute.AttributeClass, McpToolSpecsAttribute)
                    ? ToolSurfaceAdapter.Mcp
                    : (ToolSurfaceAdapter?)null)
            .OfType<ToolSurfaceAdapter>()
            .Distinct()
            .ToArray();

        return adapters.Length switch
        {
            0 => null,
            1 => adapters[0],
            _ => ToolSurfaceAdapter.Ambiguous,
        };
    }

    private static bool ReturnsGeneratedBindingManifest(
        PropertyDeclarationSyntax? property,
        string expectedManifestMetadataName)
    {
        if (property is null)
            return false;

        var expression = property.ExpressionBody?.Expression
            ?? property.AccessorList?.Accessors
                .Where(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                .Select(accessor => accessor.ExpressionBody?.Expression
                    ?? (accessor.Body?.Statements.SingleOrDefault() as ReturnStatementSyntax)?.Expression)
                .SingleOrDefault(candidate => candidate is not null);

        if (expression is not MemberAccessExpressionSyntax bindingAccess
            || bindingAccess.Name.Identifier.ValueText != "BindingRootTypes")
        {
            return false;
        }

        var receiver = bindingAccess.Expression.WithoutTrivia().ToFullString()
            .Replace("global::", string.Empty, StringComparison.Ordinal);
        var manifestSegments = expectedManifestMetadataName.Split('.');
        var localManifestName = string.Join('.', manifestSegments[^2..]);
        return expectedManifestMetadataName == receiver
            || localManifestName == receiver;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, string baseMetadataName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (HasMetadataName(current, baseMetadataName))
                return true;
        }

        return false;
    }

    private static bool HasMetadataName(INamedTypeSymbol? type, string metadataName)
        => type is not null && GetMetadataName(type) == metadataName;

    private static string GetMetadataName(INamedTypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal);

    private static bool IsProductionSourcePath(string projectDirectory, string path)
    {
        var relativePath = Path.GetRelativePath(projectDirectory, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        return !relativePath.StartsWith("bin/", StringComparison.Ordinal)
            && !relativePath.StartsWith("obj/", StringComparison.Ordinal)
            && !relativePath.Contains("/bin/", StringComparison.Ordinal)
            && !relativePath.Contains("/obj/", StringComparison.Ordinal);
    }

    private static IReadOnlyList<MetadataReference> PlatformReferences { get; } =
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CrestCreates.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private const string GuardSymbols = """
        namespace CrestCreates.Agent.Tools
        {
            public interface IAgentToolJsonContextContributor { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class AgentToolSpecsAttribute : System.Attribute
            {
                public bool GenerateDescriptorProviderRegistration { get; set; }
            }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class AgentToolSpecAttribute(string id) : System.Attribute
            {
                public System.Type? InputType { get; set; }
                public System.Type? OutputType { get; set; }
            }
        }
        namespace CrestCreates.Mcp.Abstractions
        {
            public interface IMcpToolJsonContextContributor { }
        }
        namespace CrestCreates.Mcp
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class McpToolSpecsAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class McpToolSpecAttribute(string id) : System.Attribute
            {
                public System.Type? InputType { get; set; }
                public System.Type? OutputType { get; set; }
            }
        }
        namespace CrestCreates.Core.Abstractions.Serialization
        {
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class JsonContractSurfaceAttribute(System.Type surfaceType) : System.Attribute { }
        }
        """;

    private const string GuardGlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private sealed record ToolJsonContractOwnership(
        string ContextMetadataName,
        string ContributorMetadataName,
        string ManifestMetadataName,
        ToolSurfaceAdapter Adapter);

    private sealed record ContributorContract(
        string MetadataName,
        PropertyDeclarationSyntax? BindingRootProperty,
        string FilePath);

    private sealed record ToolContextContract(
        string MetadataName,
        ToolSurfaceAdapter Adapter,
        bool HasHandwrittenJsonSerializable,
        string FilePath);

    private sealed class RepositoryAnalysis
    {
        public List<ContributorContract> Contributors { get; } = [];
        public List<ToolContextContract> Contexts { get; } = [];

        public void Deduplicate()
        {
            var contributors = Contributors
                .DistinctBy(item => item.MetadataName, StringComparer.Ordinal)
                .ToArray();
            var contexts = Contexts
                .DistinctBy(item => item.MetadataName, StringComparer.Ordinal)
                .ToArray();
            Contributors.Clear();
            Contributors.AddRange(contributors);
            Contexts.Clear();
            Contexts.AddRange(contexts);
        }
    }

    private enum ToolSurfaceAdapter
    {
        Agent,
        Mcp,
        Ambiguous,
    }
}
