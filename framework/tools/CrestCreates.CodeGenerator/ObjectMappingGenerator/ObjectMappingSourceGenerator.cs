using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.ObjectMappingGenerator
{
    [Generator]
    public sealed class ObjectMappingSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var mappingDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidate(node),
                    transform: static (ctx, _) => GetMappingDeclarations(ctx))
                .Where(static x => !x.IsDefaultOrEmpty)
                .SelectMany(static (x, _) => x)
                .Collect();

            context.RegisterSourceOutput(mappingDeclarations, ExecuteGeneration);
        }

        private static bool IsCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax { AttributeLists.Count: > 0 } classDecl
                && classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                && classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        }

        private static ImmutableArray<MappingDeclaration> GetMappingDeclarations(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol == null)
                return ImmutableArray<MappingDeclaration>.Empty;

            var attributes = symbol.GetAttributes().Where(HasGenerateObjectMappingAttribute).ToArray();
            if (attributes.Length == 0)
                return ImmutableArray<MappingDeclaration>.Empty;

            var declarations = ImmutableArray.CreateBuilder<MappingDeclaration>();

            foreach (var attribute in attributes)
            {
                if (attribute.ConstructorArguments.Length < 2)
                    continue;

                var sourceType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
                var targetType = attribute.ConstructorArguments[1].Value as INamedTypeSymbol;

                if (sourceType == null || targetType == null)
                    continue;

                var direction = MapDirection.Both;
                var directionArg = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Direction");
                if (directionArg.Value.Value is int dirValue)
                {
                    direction = (MapDirection)dirValue;
                }

                declarations.Add(new MappingDeclaration
                {
                    SourceType = sourceType,
                    TargetType = targetType,
                    MapperClassName = symbol.Name,
                    Namespace = symbol.ContainingNamespace.ToDisplayString(),
                    Direction = direction,
                    Location = classDeclaration.GetLocation()
                });
            }

            return declarations.ToImmutable();
        }

        private static bool HasGenerateObjectMappingAttribute(AttributeData attr)
        {
            return attr.AttributeClass != null && (
                attr.AttributeClass.Name == "GenerateObjectMappingAttribute" ||
                attr.AttributeClass.Name == "GenerateObjectMapping" ||
                attr.AttributeClass.ToDisplayString().EndsWith(".GenerateObjectMappingAttribute") ||
                attr.AttributeClass.ToDisplayString().EndsWith(".GenerateObjectMapping"));
        }

        private void ExecuteGeneration(
            SourceProductionContext context,
            ImmutableArray<MappingDeclaration> declarations)
        {
            if (declarations.IsDefaultOrEmpty)
                return;

            var resolver = new ObjectMappingRuleResolver();
            var writer = new ObjectMappingCodeWriter();

            // Group declarations by class name to detect duplicates needing disambiguation
            var groups = declarations
                .GroupBy(d => d.MapperClassName);

            var usedFileNames = new HashSet<string>();

            foreach (var group in groups)
            {
                var declarationsInGroup = group.ToArray();
                var needsDisambiguation = declarationsInGroup.Length > 1;

                foreach (var declaration in declarationsInGroup)
                {
                    var model = resolver.Resolve(declaration);

                    foreach (var diagnostic in model.Diagnostics)
                    {
                        context.ReportDiagnostic(diagnostic);
                    }

                    if (model.IsValid)
                    {
                        var source = writer.Write(model);
                        var fileName = declaration.MapperClassName;
                        if (needsDisambiguation)
                        {
                            // Use direction suffix; for Both, add an index to avoid collisions
                            var suffix = declaration.Direction != MapDirection.Both
                                ? $".{declaration.Direction}"
                                : $".{declaration.Direction}{usedFileNames.Count}";
                            fileName += suffix;
                        }
                        fileName += ".g.cs";
                        if (!usedFileNames.Add(fileName))
                        {
                            // Guard: if still duplicate, append index
                            var i = 1;
                            while (!usedFileNames.Add($"{declaration.MapperClassName}.{i}.g.cs"))
                                i++;
                            fileName = $"{declaration.MapperClassName}.{i}.g.cs";
                        }
                        context.AddSource(fileName, SourceText.From(source, System.Text.Encoding.UTF8));
                    }
                }
            }
        }
    }
}
