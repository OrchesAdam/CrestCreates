// framework/tools/CrestCreates.CodeGenerator/KafkaGenerator/KafkaSubscriptionSourceGenerator.cs
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.KafkaGenerator
{
    [Generator]
    public sealed class KafkaSubscriptionSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var subscriptionMethods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidate(node),
                    transform: static (ctx, _) => GetSubscriptionInfo(ctx))
                .Where(static x => x is not null)
                .Collect();

            context.RegisterSourceOutput(subscriptionMethods, ExecuteGeneration);
        }

        private static bool IsCandidate(SyntaxNode node)
        {
            return node is MethodDeclarationSyntax { AttributeLists.Count: > 0 };
        }

        private static KafkaSubscriptionInfo? GetSubscriptionInfo(GeneratorSyntaxContext context)
        {
            var methodDeclaration = (MethodDeclarationSyntax)context.Node;
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;

            if (methodSymbol == null)
                return null;

            // Find the KafkaSubscribe attribute
            var attribute = methodSymbol.GetAttributes().FirstOrDefault(HasKafkaSubscribeAttribute);
            if (attribute == null)
                return null;

            // Extract topic from attribute constructor argument
            string? topic = null;
            if (attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string topicValue)
            {
                topic = topicValue;
            }

            if (string.IsNullOrEmpty(topic))
                return null;

            // Get the containing type (handler class)
            var containingType = methodSymbol.ContainingType;
            if (containingType == null)
                return null;

            // Get event type from first parameter
            var parameters = methodSymbol.Parameters;
            if (parameters.Length == 0)
                return null;

            var eventType = parameters[0].Type;
            if (eventType == null)
                return null;

            // Extract optional named arguments
            string? consumerGroup = null;
            var maxPollRecords = 500;

            foreach (var namedArg in attribute.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "ConsumerGroup":
                        if (namedArg.Value.Value is string consumerGroupValue)
                            consumerGroup = consumerGroupValue;
                        break;
                    case "MaxPollRecords":
                        if (namedArg.Value.Value is int maxPollValue)
                            maxPollRecords = maxPollValue;
                        break;
                }
            }

            return new KafkaSubscriptionInfo
            {
                Topic = topic!, // Already verified non-null above
                EventTypeFullName = eventType.ToDisplayString(),
                HandlerTypeFullName = containingType.ToDisplayString(),
                HandlerMethodName = methodSymbol.Name,
                ConsumerGroup = consumerGroup,
                MaxPollRecords = maxPollRecords
            };
        }

        private static bool HasKafkaSubscribeAttribute(AttributeData attr)
        {
            if (attr.AttributeClass == null)
                return false;

            var name = attr.AttributeClass.Name;
            var fullName = attr.AttributeClass.ToDisplayString();

            return name == "KafkaSubscribeAttribute" ||
                   name == "KafkaSubscribe" ||
                   fullName.EndsWith(".KafkaSubscribeAttribute") ||
                   fullName.EndsWith(".KafkaSubscribe");
        }

        private void ExecuteGeneration(
            SourceProductionContext context,
            ImmutableArray<KafkaSubscriptionInfo?> subscriptions)
        {
            if (subscriptions.IsDefaultOrEmpty)
                return;

            var validSubscriptions = subscriptions
                .Where(x => x != null)
                .Cast<KafkaSubscriptionInfo>()
                .ToList();

            if (validSubscriptions.Count == 0)
                return;

            var model = new KafkaSubscriptionModel
            {
                Namespace = "CrestCreates.EventBus.Kafka.Generated",
                Subscriptions = validSubscriptions
            };

            var writer = new KafkaSubscriptionCodeWriter();
            var source = writer.WriteRegistry(model);

            context.AddSource(
                "KafkaSubscriptionRegistry.g.cs",
                SourceText.From(source, System.Text.Encoding.UTF8));
        }
    }
}
