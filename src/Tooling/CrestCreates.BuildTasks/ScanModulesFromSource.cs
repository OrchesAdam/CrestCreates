using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Globalization;
using System.Text.Json;

namespace CrestCreates.BuildTasks;

public class ScanModulesFromSource : Microsoft.Build.Utilities.Task
{
    private const string ModuleAttributeNamespace = "CrestCreates.Domain.Shared.Attributes";
    private const string ModuleAttributeShortName = "CrestModule";
    private const string ModuleAttributeLongName = "CrestModuleAttribute";

    public string SourceFiles { get; set; } = string.Empty;

    public string DefineConstants { get; set; } = string.Empty;

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

            var hasGlobalFormalUsing = trees
                .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>())
                .Any(IsFormalGlobalUsing);

            var modules = new SortedDictionary<string, CollectModuleManifests.ModuleManifest>(StringComparer.Ordinal);
            foreach (var tree in trees)
            {
                foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var attributes = declaration.AttributeLists.SelectMany(a => a.Attributes)
                        .Where(a => IsFormalModuleAttribute(a, declaration, hasGlobalFormalUsing)).ToList();
                    if (attributes.Count == 0) continue;
                    if (attributes.Count != 1) throw new InvalidDataException($"Duplicate CrestModule attribute: {declaration.Identifier}");
                    if (declaration.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().Any(t => t.TypeParameterList != null))
                        throw new InvalidDataException($"Module must be a closed type: {declaration.Identifier}");

                    var ns = string.Join(".", declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));
                    var typeName = string.Join(".", declaration.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().Reverse().Select(t => t.Identifier.ValueText));
                    var module = new CollectModuleManifests.ModuleManifest
                    {
                        Name = declaration.Identifier.ValueText, Namespace = ns,
                        FullName = string.IsNullOrEmpty(ns) ? typeName : ns + "." + typeName,
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
                                if (!int.TryParse(argument.Expression.WithoutTrivia().ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var order))
                                    throw new InvalidDataException($"Invalid module Order: {argument}");
                                module.Order = order;
                                break;
                            case "AutoRegisterServices":
                                if (argument.Expression is not LiteralExpressionSyntax literal || literal.Token.Value is not bool value)
                                    throw new InvalidDataException($"Invalid module AutoRegisterServices: {argument}");
                                module.AutoRegisterServices = value;
                                break;
                            case null:
                            case "dependsOn":
                            case "DependsOn":
                                ReadDependencies(argument.Expression, declaration, module.DependsOn);
                                break;
                            default:
                                throw new InvalidDataException($"Unknown module argument: {name}");
                        }
                    }
                    module.DependsOn = module.DependsOn.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                    if (!modules.TryAdd(module.FullName, module)) throw new InvalidDataException($"Duplicate module identity: {module.FullName}");
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(OutputPath))!);
            File.WriteAllText(OutputPath, JsonSerializer.Serialize(modules.Values, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    private static bool IsFormalModuleAttribute(
        AttributeSyntax attribute,
        ClassDeclarationSyntax declaration,
        bool hasGlobalFormalUsing)
    {
        var raw = attribute.Name.WithoutTrivia().ToString().Replace("global::", "", StringComparison.Ordinal);
        var first = raw.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var alias = UsingsFor(declaration).FirstOrDefault(u => u.Alias?.Name.Identifier.ValueText == first);
        var normalized = alias?.Name is { } target
            ? NormalizeName(target.ToString()) + raw[first.Length..].Replace("::", ".", StringComparison.Ordinal)
            : raw.Replace("::", ".", StringComparison.Ordinal);
        normalized = normalized.Trim('.');

        if (normalized is ModuleAttributeShortName or ModuleAttributeLongName)
            return HasFormalUsing(declaration) || hasGlobalFormalUsing;

        return normalized == $"{ModuleAttributeNamespace}.{ModuleAttributeShortName}"
            || normalized == $"{ModuleAttributeNamespace}.{ModuleAttributeLongName}";
    }

    private static bool IsFormalGlobalUsing(UsingDirectiveSyntax directive)
        => directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)
            && directive.Alias is null
            && NormalizeName(directive.Name?.ToString()) == ModuleAttributeNamespace;

    private static bool HasFormalUsing(ClassDeclarationSyntax declaration)
        => UsingsFor(declaration).Any(u => u.Alias is null
            && NormalizeName(u.Name?.ToString()) == ModuleAttributeNamespace);

    private static IEnumerable<UsingDirectiveSyntax> UsingsFor(SyntaxNode node)
        => node.Ancestors().SelectMany(n => n switch
        {
            BaseNamespaceDeclarationSyntax ns => ns.Usings.AsEnumerable(),
            CompilationUnitSyntax root => root.Usings.AsEnumerable(),
            _ => Enumerable.Empty<UsingDirectiveSyntax>()
        });

    private static string NormalizeName(string? name)
        => (name ?? string.Empty).Replace("global::", "", StringComparison.Ordinal)
            .Replace("::", ".", StringComparison.Ordinal).Trim();

    private static string ResolveAlias(NameSyntax name, ClassDeclarationSyntax declaration)
    {
        var text = NormalizeName(name.WithoutTrivia().ToString());
        var first = text.Split('.', ':')[0];
        var alias = UsingsFor(declaration).FirstOrDefault(u => u.Alias?.Name.Identifier.ValueText == first);
        return alias?.Name is { } target
            ? NormalizeName(target.ToString()) + text[first.Length..].Replace("::", ".", StringComparison.Ordinal)
            : text;
    }

    private static void ReadDependencies(ExpressionSyntax expression, ClassDeclarationSyntax declaration, List<string> dependencies)
    {
        if (expression is TypeOfExpressionSyntax typeOf && typeOf.Type is NameSyntax name)
        {
            dependencies.Add(ResolveAlias(name, declaration));
            return;
        }
        var initializer = expression switch
        {
            ArrayCreationExpressionSyntax array => array.Initializer,
            ImplicitArrayCreationExpressionSyntax array => array.Initializer,
            _ => null
        };
        if (initializer == null) throw new InvalidDataException($"Module dependencies must be typeof expressions or arrays: {expression}");
        foreach (var item in initializer.Expressions) ReadDependencies(item, declaration, dependencies);
    }
}
