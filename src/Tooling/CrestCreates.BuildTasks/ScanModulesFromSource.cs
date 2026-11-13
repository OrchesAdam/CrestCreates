using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Globalization;
using System.Text.Json;

namespace CrestCreates.BuildTasks;

public class ScanModulesFromSource : Microsoft.Build.Utilities.Task
{
    public string SourceFiles { get; set; } = string.Empty;

    public string DefineConstants { get; set; } = string.Empty;

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            var symbols = DefineConstants.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(symbol => symbol.Trim())
                .Where(symbol => symbol.Length != 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview).WithPreprocessorSymbols(symbols);
            var modules = new SortedDictionary<string, CollectModuleManifests.ModuleManifest>(StringComparer.Ordinal);
            foreach (var file in SourceFiles.Split(';', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal))
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file);
                var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                if (errors.Count != 0)
                    throw new InvalidDataException(string.Join(Environment.NewLine, errors));

                foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var attributes = declaration.AttributeLists.SelectMany(a => a.Attributes)
                        .Where(a => IsModuleAttribute(ResolveAlias(a.Name, declaration))).ToList();
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

    private static bool IsModuleAttribute(string name) => name.Split('.').Last() is "CrestModule" or "CrestModuleAttribute";

    private static string ResolveAlias(NameSyntax name, ClassDeclarationSyntax declaration)
    {
        var text = name.WithoutTrivia().ToString().Replace("global::", "", StringComparison.Ordinal);
        var first = text.Split('.', ':')[0];
        var usings = declaration.Ancestors().SelectMany(n => n switch
        {
            BaseNamespaceDeclarationSyntax ns => ns.Usings.AsEnumerable(),
            CompilationUnitSyntax root => root.Usings.AsEnumerable(),
            _ => Enumerable.Empty<UsingDirectiveSyntax>()
        });
        var alias = usings.FirstOrDefault(u => u.Alias?.Name.Identifier.ValueText == first);
        return alias?.Name is { } target ? target.ToString().Replace("global::", "", StringComparison.Ordinal) + text[first.Length..].Replace("::", ".", StringComparison.Ordinal) : text;
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
