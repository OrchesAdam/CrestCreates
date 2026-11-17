using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;

namespace CrestCreates.BuildTasks;

public class ScanModulesFromSource : Microsoft.Build.Utilities.Task
{
    private const string ModuleAttributeNamespace = "CrestCreates.Domain.Shared.Attributes";
    private const string ModuleAttributeName = "CrestModule";
    private const string ModuleAttributeLongName = "CrestModuleAttribute";

    public string SourceFiles { get; set; } = string.Empty;

    public string DefineConstants { get; set; } = string.Empty;

    public ITaskItem[] ReferencePaths { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            var symbols = DefineConstants
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(symbol => symbol.Trim())
                .Where(symbol => symbol.Length != 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview).WithPreprocessorSymbols(symbols);
            var trees = new List<SyntaxTree>();
            foreach (var file in SourceFiles.Split(';', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal))
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file);
                var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                if (errors.Count != 0)
                    throw new InvalidDataException(string.Join(Environment.NewLine, errors));
                trees.Add(tree);
            }

            var compilation = CSharpCompilation.Create(
                "CrestCreates.ModuleDiscovery",
                trees,
                ReferencePaths.Select(reference => MetadataReference.CreateFromFile(
                    Path.GetFullPath(reference.ItemSpec),
                    MetadataReferenceProperties.Assembly.WithAliases(reference.GetMetadata("Aliases")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var formalAttributes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols.Prepend(compilation.Assembly))
                if (assembly.GetTypeByMetadataName($"{ModuleAttributeNamespace}.{ModuleAttributeLongName}") is { } attribute)
                    formalAttributes.Add(attribute);

            var semanticModels = new Dictionary<SyntaxTree, SemanticModel>();
            foreach (var tree in trees) semanticModels.Add(tree, compilation.GetSemanticModel(tree));
            var modules = new SortedDictionary<string, CollectModuleManifests.ModuleManifest>(StringComparer.Ordinal);
            foreach (var tree in trees)
            {
                var semanticModel = semanticModels[tree];
                foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var attributes = new List<AttributeSyntax>();
                    foreach (var attribute in declaration.AttributeLists.SelectMany(list => list.Attributes))
                    {
                        var attributeType = semanticModel.GetTypeInfo(attribute).Type;
                        if (attributeType is null || attributeType.TypeKind == TypeKind.Error)
                        {
                            // Generated attributes bind later in CoreCompile; only visible formal candidates fail here.
                            if (!IsModuleAttributeCandidate(attribute.Name, attribute, semanticModel, semanticModels, formalAttributes, new HashSet<UsingDirectiveSyntax>())) continue;
                            var location = attribute.GetLocation().GetLineSpan();
                            Log.LogError(null, "CCMB001", null, location.Path,
                                location.StartLinePosition.Line + 1, location.StartLinePosition.Character + 1,
                                location.EndLinePosition.Line + 1, location.EndLinePosition.Character + 1,
                                "Cannot bind attribute type '{0}'. Check source and resolved references.", attribute.Name);
                            return false;
                        }
                        if (formalAttributes.Contains(attributeType)) attributes.Add(attribute);
                    }
                    if (attributes.Count == 0) continue;
                    if (attributes.Count != 1) throw new InvalidDataException($"Duplicate CrestModule attribute: {declaration.Identifier}");
                    if (declaration.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().Any(t => t.TypeParameterList != null))
                        throw new InvalidDataException($"Module must be a closed type: {declaration.Identifier}");

                    var moduleType = semanticModel.GetDeclaredSymbol(declaration)!;
                    var attributeValues = moduleType.GetAttributes()
                        .Single(attribute => attribute.ApplicationSyntaxReference is { } syntax
                            && syntax.SyntaxTree == tree && syntax.Span == attributes[0].Span)
                        .NamedArguments.ToDictionary(argument => argument.Key, argument => argument.Value, StringComparer.Ordinal);
                    var module = new CollectModuleManifests.ModuleManifest
                    {
                        Name = moduleType.Name,
                        Namespace = moduleType.ContainingNamespace.IsGlobalNamespace
                            ? string.Empty
                            : moduleType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        FullName = moduleType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        AutoRegisterServices = true
                    };
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var argument in attributes[0].ArgumentList?.Arguments ?? default)
                    {
                        var name = argument.NameEquals?.Name.Identifier.ValueText ?? argument.NameColon?.Name.Identifier.ValueText;
                        if (name != null && !seen.Add(name)) throw new InvalidDataException($"Duplicate module argument: {name}");
                        switch (name)
                        {
                            case "Order":
                                if (!attributeValues.TryGetValue(name, out var orderValue)
                                    || orderValue.Kind != TypedConstantKind.Primitive || orderValue.Value is not int)
                                    throw new InvalidDataException($"Invalid module Order: {argument}");
                                module.Order = (int)orderValue.Value;
                                break;
                            case "AutoRegisterServices":
                                if (!attributeValues.TryGetValue(name, out var autoRegisterValue)
                                    || autoRegisterValue.Kind != TypedConstantKind.Primitive || autoRegisterValue.Value is not bool)
                                    throw new InvalidDataException($"Invalid module AutoRegisterServices: {argument}");
                                module.AutoRegisterServices = (bool)autoRegisterValue.Value;
                                break;
                            case null:
                            case "dependsOn":
                            case "DependsOn":
                                ReadDependencies(argument.Expression, semanticModel, module.DependsOn);
                                break;
                            default:
                                throw new InvalidDataException($"Unknown module argument: {name}");
                        }
                    }
                    module.DependsOn = module.DependsOn.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                    if (!modules.TryAdd(module.FullName, module)) throw new InvalidDataException($"Duplicate module identity: {module.FullName}");
                }
            }
            BuildOutput.WriteIfChanged(OutputPath, JsonSerializer.Serialize(modules.Values, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    private static bool IsModuleAttributeCandidate(
        NameSyntax name,
        SyntaxNode location,
        SemanticModel semanticModel,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> semanticModels,
        HashSet<ISymbol> formalAttributes,
        HashSet<UsingDirectiveSyntax> visitedAliases)
    {
        if (HasFormalModuleName(name)) return true;
        if (name is not SimpleNameSyntax simple) return false;
        var alias = GetVisibleAlias(simple.Identifier.ValueText, location, semanticModel, semanticModels);
        if (alias is not null && IsFormalAliasTarget(alias.Value.Directive, alias.Value.Model, semanticModels, formalAttributes, visitedAliases)) return true;
        if (location is not AttributeSyntax || simple.Identifier.Text.StartsWith("@", StringComparison.Ordinal)) return false;
        var suffixedAlias = GetVisibleAlias(simple.Identifier.ValueText + "Attribute", location, semanticModel, semanticModels);
        return suffixedAlias is not null && IsFormalAliasTarget(suffixedAlias.Value.Directive, suffixedAlias.Value.Model, semanticModels, formalAttributes, visitedAliases);
    }

    private static bool IsFormalAliasTarget(
        UsingDirectiveSyntax alias,
        SemanticModel semanticModel,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> semanticModels,
        HashSet<ISymbol> formalAttributes,
        HashSet<UsingDirectiveSyntax> visitedAliases)
    {
        if (!visitedAliases.Add(alias)) return true;
        var target = semanticModel.GetDeclaredSymbol(alias)?.Target;
        if (target is not null && formalAttributes.Contains(target)) return true;
        if (target is not null and not INamespaceSymbol and not IErrorTypeSymbol) return false;
        return IsModuleAttributeCandidate(alias.Name!, alias, semanticModel, semanticModels, formalAttributes, visitedAliases);
    }

    private static bool HasFormalModuleName(NameSyntax name)
        => name switch
        {
            AliasQualifiedNameSyntax aliasQualified => IsFormalModuleName(aliasQualified.Name),
            QualifiedNameSyntax qualified => IsFormalModuleName(qualified.Right),
            SimpleNameSyntax simple => IsFormalModuleName(simple),
            _ => false
        };

    private static bool IsFormalModuleName(SimpleNameSyntax name)
        => name.Identifier.ValueText is ModuleAttributeName or ModuleAttributeLongName;

    private static (UsingDirectiveSyntax Directive, SemanticModel Model)? GetVisibleAlias(
        string name,
        SyntaxNode location,
        SemanticModel semanticModel,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> semanticModels)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var usingTargetScope = (location as UsingDirectiveSyntax)?.Parent;
        foreach (var scope in location.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
                     .Append(location.SyntaxTree.GetRoot()))
        {
            if (scope == usingTargetScope) continue;
            var usings = scope switch
            {
                CompilationUnitSyntax compilation => compilation.Usings,
                BaseNamespaceDeclarationSyntax declaration => declaration.Usings,
                _ => default
            };
            foreach (var directive in usings)
                if (directive.Alias is { } alias)
                {
                    var aliasName = alias.Name.Identifier.ValueText;
                    if (aliasName == name && names.Add(aliasName)) return (directive, semanticModel);
                    names.Add(aliasName);
                }
        }
        if (usingTargetScope is CompilationUnitSyntax) return null;
        foreach (var (tree, model) in semanticModels)
            foreach (var directive in tree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>())
                if (directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) && directive.Alias is { } alias
                    && names.Add(alias.Name.Identifier.ValueText) && alias.Name.Identifier.ValueText == name)
                    return (directive, model);
        return null;
    }

    private static void ReadDependencies(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        List<string> dependencies)
    {
        if (expression is TypeOfExpressionSyntax typeOf)
        {
            if (semanticModel.GetTypeInfo(typeOf.Type).Type is not INamedTypeSymbol dependency
                || dependency.TypeKind == TypeKind.Error)
                throw new InvalidDataException($"Module dependency type could not be resolved: {typeOf.Type}");
            dependencies.Add(dependency.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
            return;
        }
        var initializer = expression switch
        {
            ArrayCreationExpressionSyntax array => array.Initializer,
            ImplicitArrayCreationExpressionSyntax array => array.Initializer,
            _ => null
        };
        if (initializer == null) throw new InvalidDataException($"Module dependencies must be typeof expressions or arrays: {expression}");
        foreach (var item in initializer.Expressions) ReadDependencies(item, semanticModel, dependencies);
    }
}
