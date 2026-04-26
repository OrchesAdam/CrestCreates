// framework/tools/CrestCreates.CodeGenerator/CompensationExecutorGenerator/CompensationExecutorSourceGenerator.cs
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.CompensationExecutorGenerator
{
    [Generator]
    public sealed class CompensationExecutorSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var executorClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidate(node),
                    transform: static (ctx, _) => GetExecutorInfo(ctx))
                .Where(static x => x is not null)
                .Collect();

            context.RegisterSourceOutput(executorClasses, ExecuteGeneration);
        }

        private static bool IsCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };
        }

        private static ExecutorInfo? GetExecutorInfo(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol == null)
                return null;

            var attribute = symbol.GetAttributes().FirstOrDefault(HasCompensationExecutorAttribute);
            if (attribute == null)
                return null;

            // Find Name property
            var nameProperty = symbol.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(p => p.Name == "Name");

            var nameValue = nameProperty != null
                ? $"{symbol.Name}.Name"
                : $"\"{symbol.Name.Replace("CompensationExecutor", "").Replace("Executor", "")}\"";

            return new ExecutorInfo
            {
                ClassName = symbol.Name,
                FullName = symbol.ToDisplayString(),
                NameProperty = nameValue
            };
        }

        private static bool HasCompensationExecutorAttribute(AttributeData attr)
        {
            return attr.AttributeClass != null && (
                attr.AttributeClass.Name == "CompensationExecutorAttribute" ||
                attr.AttributeClass.Name == "CompensationExecutor" ||
                attr.AttributeClass.ToDisplayString().EndsWith(".CompensationExecutorAttribute") ||
                attr.AttributeClass.ToDisplayString().EndsWith(".CompensationExecutor"));
        }

        private void ExecuteGeneration(
            SourceProductionContext context,
            ImmutableArray<ExecutorInfo?> executors)
        {
            if (executors.IsDefaultOrEmpty)
                return;

            var validExecutors = executors
                .Where(x => x != null)
                .Cast<ExecutorInfo>()
                .ToList();

            if (validExecutors.Count == 0)
                return;

            // Group by namespace (use first executor's namespace)
            var firstExecutor = validExecutors.First();
            var ns = GetNamespace(firstExecutor.FullName);

            var model = new CompensationExecutorModel
            {
                Namespace = ns,
                Executors = validExecutors
            };

            var writer = new CompensationExecutorCodeWriter();
            var source = writer.WriteRegistry(model);

            context.AddSource(
                "CompensationExecutorRegistry.g.cs",
                SourceText.From(source, System.Text.Encoding.UTF8));
        }

        private static string GetNamespace(string fullName)
        {
            var lastDot = fullName.LastIndexOf('.');
            return lastDot > 0 ? fullName.Substring(0, lastDot) : "Generated";
        }
    }
}