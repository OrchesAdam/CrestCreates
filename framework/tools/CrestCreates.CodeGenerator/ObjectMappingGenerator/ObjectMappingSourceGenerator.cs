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
                    transform: static (ctx, _) => GetMappingDeclaration(ctx))
                .Where(static x => x is not null)
                .Collect();

            context.RegisterSourceOutput(mappingDeclarations, ExecuteGeneration);
        }

        private static bool IsCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax { AttributeLists.Count: > 0 } classDecl
                && classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                && classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        }

        private static MappingDeclaration? GetMappingDeclaration(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol == null)
                return null;

            var attribute = symbol.GetAttributes().FirstOrDefault(HasGenerateObjectMappingAttribute);
            if (attribute == null)
                return null;

            // Extract source and target types from attribute constructor arguments
            if (attribute.ConstructorArguments.Length < 2)
                return null;

            var sourceType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
            var targetType = attribute.ConstructorArguments[1].Value as INamedTypeSymbol;

            if (sourceType == null || targetType == null)
                return null;

            // Extract Direction from named arguments
            var direction = MapDirection.Both;
            var directionArg = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Direction");
            if (directionArg.Value.Value is int dirValue)
            {
                direction = (MapDirection)dirValue;
            }

            return new MappingDeclaration
            {
                SourceType = sourceType,
                TargetType = targetType,
                MapperClassName = symbol.Name,
                Namespace = symbol.ContainingNamespace.ToDisplayString(),
                Direction = direction,
                Location = classDeclaration.GetLocation()
            };
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
            ImmutableArray<MappingDeclaration?> declarations)
        {
            if (declarations.IsDefaultOrEmpty)
                return;

            var resolver = new ObjectMappingRuleResolver();
            var writer = new ObjectMappingCodeWriter();

            foreach (var declaration in declarations)
            {
                if (declaration == null)
                    continue;

                var model = resolver.Resolve(declaration);

                // Report diagnostics
                foreach (var diagnostic in model.Diagnostics)
                {
                    context.ReportDiagnostic(diagnostic);
                }

                // Generate source if model is valid
                if (model.IsValid)
                {
                    var source = writer.Write(model);
                    context.AddSource(
                        $"{declaration.MapperClassName}.g.cs",
                        SourceText.From(source, System.Text.Encoding.UTF8));
                }
            }
        }
    }
}
