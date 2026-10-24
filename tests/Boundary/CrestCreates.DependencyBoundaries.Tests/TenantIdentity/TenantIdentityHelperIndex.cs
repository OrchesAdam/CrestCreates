using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.DependencyBoundaries.Tests;

internal sealed record TenantIdentityForeignHelper(
    SemanticModel Model,
    IMethodSymbol Symbol,
    MethodDeclarationSyntax Syntax,
    string AssemblyName);

/// <summary>
/// Indexes first-party method declarations that mention tenants, so a helper living in another project
/// can still be summarised from its real source instead of being reported as undecidable. Projects are
/// compiled lazily: only the owning project of a helper that actually reaches an identity slot is built.
/// </summary>
internal sealed class TenantIdentityHelperIndex
{
    private readonly Dictionary<string, List<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly Func<string, TenantProjectContext> _resolveContext;

    public TenantIdentityHelperIndex(Func<string, TenantProjectContext> resolveContext) => _resolveContext = resolveContext;

    public int RegisteredMembers => _entries.Values.Sum(list => list.Count);

    public void RegisterProject(TenantProjectInput project)
    {
        if (project.ReferenceError is not null)
        {
            return;
        }

        var parseOptions = TenantIdentitySymbols.ParseOptions(project);
        foreach (var source in project.Sources)
        {
            if (!TenantIdentityPorts.Normalize(source.Text).Contains("tenant", StringComparison.Ordinal))
            {
                continue;
            }

            var root = CSharpSyntaxTree.ParseText(SourceText.From(source.Text), parseOptions, path: source.RelativePath).GetRoot();
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var containing = FirstTypeName(method);
                if (containing is null)
                {
                    continue;
                }

                Add($"{containing}.{method.Identifier.ValueText}/{method.ParameterList.Parameters.Count}",
                    new Entry(project.Project, source.RelativePath, containing, method.Identifier.ValueText,
                        method.ParameterList.Parameters.Count,
                        method.GetLocation().GetLineSpan().StartLinePosition.Line));
            }
        }
    }

    public TenantIdentityForeignHelper? Resolve(string currentProject, string typeFullName, string methodName, int parameterCount)
    {
        var key = $"{typeFullName}.{methodName}/{parameterCount}";
        if (!_entries.TryGetValue(key, out var candidates))
        {
            return null;
        }

        foreach (var entry in candidates)
        {
            if (string.Equals(entry.Project, currentProject, StringComparison.Ordinal))
            {
                continue;
            }

            var context = _resolveContext(entry.Project);

            var tree = context.TreesByPath.GetValueOrDefault(entry.RelativePath);
            if (tree is null)
            {
                continue;
            }

            var model = context.Models[tree];
            var declaration = tree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(candidate =>
                    candidate.Identifier.ValueText == entry.MethodName
                    && candidate.ParameterList.Parameters.Count == entry.ParameterCount
                    && candidate.GetLocation().GetLineSpan().StartLinePosition.Line == entry.Line);

            if (declaration is not null && model.GetDeclaredSymbol(declaration) is IMethodSymbol symbol)
            {
                return new TenantIdentityForeignHelper(model, symbol, declaration, context.Input.AssemblyName);
            }
        }

        return null;
    }

    private void Add(string key, Entry entry)
    {
        if (!_entries.TryGetValue(key, out var list))
        {
            list = [];
            _entries[key] = list;
        }

        list.Add(entry);
    }

    private static string? FirstTypeName(MethodDeclarationSyntax method)
    {
        var parts = new List<string>();
        foreach (var ancestor in method.Ancestors())
        {
            switch (ancestor)
            {
                case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                    parts.Insert(0, namespaceDeclaration.Name.ToString());
                    break;
                case TypeDeclarationSyntax type:
                    parts.Insert(0, type.Identifier.ValueText);
                    break;
            }
        }

        return parts.Count == 0 ? null : string.Join(".", parts);
    }

    private sealed record Entry(
        string Project,
        string RelativePath,
        string ContainingType,
        string MethodName,
        int ParameterCount,
        int Line);
}

/// <summary>
/// One project parsed and compiled once, with a model per tree, so helper bodies can be read from their
/// own compilation context rather than from metadata or a stub.
/// </summary>
internal sealed class TenantProjectContext
{
    private TenantProjectContext(
        TenantProjectInput input,
        CSharpCompilation compilation,
        Dictionary<string, SyntaxTree> treesByPath,
        Dictionary<SyntaxTree, SemanticModel> models)
    {
        Input = input;
        Compilation = compilation;
        TreesByPath = treesByPath;
        Models = models;
    }

    public CSharpCompilation Compilation { get; }

    public TenantProjectInput Input { get; }

    public Dictionary<string, SyntaxTree> TreesByPath { get; }

    public Dictionary<SyntaxTree, SemanticModel> Models { get; }

    public SemanticModel? ModelFor(SyntaxTree tree) => Models.GetValueOrDefault(tree);

    public static TenantProjectContext Build(TenantProjectInput project)
    {
        var parseOptions = TenantIdentitySymbols.ParseOptions(project);
        var treesByPath = project.Sources.ToDictionary(
            source => source.RelativePath,
            source => (SyntaxTree)CSharpSyntaxTree.ParseText(
                SourceText.From(source.Text),
                parseOptions,
                path: source.RelativePath),
            StringComparer.Ordinal);

        var resolutionTrees = project.GeneratedSources
            .Select(source => (SyntaxTree)CSharpSyntaxTree.ParseText(
                SourceText.From(source.Text),
                parseOptions,
                path: source.RelativePath))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            project.AssemblyName,
            TenantIdentitySymbols.GlobalUsings(project),
            project.References,
            TenantIdentitySymbols.Options(project));

        foreach (var tree in treesByPath.Values.Concat(resolutionTrees))
        {
            compilation = compilation.AddSyntaxTrees(tree);
        }

        var models = new Dictionary<SyntaxTree, SemanticModel>(SyntaxTreeIdentityComparer.Instance);
        foreach (var tree in treesByPath.Values.Concat(resolutionTrees))
        {
            models[tree] = compilation.GetSemanticModel(tree);
        }

        return new TenantProjectContext(project, compilation, treesByPath, models);
    }
}

internal sealed class SyntaxTreeIdentityComparer : IEqualityComparer<SyntaxTree>
{
    public static readonly SyntaxTreeIdentityComparer Instance = new();

    public bool Equals(SyntaxTree? left, SyntaxTree? right) => ReferenceEquals(left, right);

    public int GetHashCode(SyntaxTree tree) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(tree);
}
