// framework/tools/CrestCreates.CodeGenerator/RabbitMqGenerator/RabbitMqSubscriptionSourceGenerator.cs
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.RabbitMqGenerator
{
    [Generator]
    public sealed class RabbitMqSubscriptionSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var methodSubscriptions = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidate(node),
                    transform: static (ctx, _) => GetSubscriptionInfo(ctx))
                .Where(static x => x is not null)
                .Collect();

            context.RegisterSourceOutput(methodSubscriptions, ExecuteGeneration);
        }

        private static bool IsCandidate(SyntaxNode node)
        {
            return node is MethodDeclarationSyntax { AttributeLists.Count: > 0 };
        }

        private static SubscriptionInfo? GetSubscriptionInfo(GeneratorSyntaxContext context)
        {
            var methodDeclaration = (MethodDeclarationSyntax)context.Node;
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;

            if (methodSymbol == null)
                return null;

            // Find the RabbitMqSubscribe attribute
            var attribute = methodSymbol.GetAttributes().FirstOrDefault(HasRabbitMqSubscribeAttribute);
            if (attribute == null)
                return null;

            // Extract EventType from constructor argument
            string eventType = string.Empty;
            if (attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string eventTypeValue)
            {
                eventType = eventTypeValue;
            }

            if (string.IsNullOrEmpty(eventType))
                return null;

            // Get the containing type (handler class)
            var handlerType = methodSymbol.ContainingType;
            if (handlerType == null)
                return null;

            // Extract optional named arguments
            string exchange = "crestcreates.events";
            string queue = $"crestcreates.{eventType}";
            int prefetchCount = 10;

            foreach (var namedArg in attribute.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "Exchange":
                        if (namedArg.Value.Value is string exchangeValue)
                            exchange = exchangeValue;
                        break;
                    case "Queue":
                        if (namedArg.Value.Value is string queueValue)
                            queue = queueValue;
                        break;
                    case "PrefetchCount":
                        if (namedArg.Value.Value is int prefetchValue)
                            prefetchCount = prefetchValue;
                        break;
                }
            }

            return new SubscriptionInfo
            {
                EventType = eventType,
                HandlerType = handlerType.ToDisplayString(),
                HandlerMethod = methodSymbol.Name,
                Exchange = exchange,
                Queue = queue,
                PrefetchCount = prefetchCount
            };
        }

        private static bool HasRabbitMqSubscribeAttribute(AttributeData attr)
        {
            if (attr.AttributeClass == null)
                return false;

            var name = attr.AttributeClass.Name;
            var fullName = attr.AttributeClass.ToDisplayString();

            return name == "RabbitMqSubscribeAttribute" ||
                   name == "RabbitMqSubscribe" ||
                   fullName.EndsWith(".RabbitMqSubscribeAttribute") ||
                   fullName.EndsWith(".RabbitMqSubscribe");
        }

        private void ExecuteGeneration(
            SourceProductionContext context,
            ImmutableArray<SubscriptionInfo?> subscriptions)
        {
            if (subscriptions.IsDefaultOrEmpty)
                return;

            var validSubscriptions = subscriptions
                .Where(x => x != null)
                .Cast<SubscriptionInfo>()
                .ToList();

            if (validSubscriptions.Count == 0)
                return;

            // Use a fixed namespace for the generated code
            var model = new RabbitMqSubscriptionModel
            {
                Namespace = "CrestCreates.EventBus.RabbitMQ.Generated",
                Subscriptions = validSubscriptions
            };

            var writer = new RabbitMqSubscriptionCodeWriter();
            var source = writer.WriteRegistry(model);

            context.AddSource(
                "RabbitMqSubscriptionRegistry.g.cs",
                SourceText.From(source, System.Text.Encoding.UTF8));
        }
    }
}
