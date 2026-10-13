using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.RabbitMqGenerator;

/// <summary>
/// Generates the per-assembly RabbitMQ subscription contribution: closed typed invokers, handler DI
/// bindings and a public AddRabbitMqSubscriptions() extension that the host must call explicitly. The
/// canonical event name is a reference resolved against the frozen event registry at freeze time; the
/// payload type comes from the handler method parameter and is never parsed from a string. No runtime
/// reflection, no assembly scanning and no fallback when the contributor is not activated.
/// </summary>
[Generator]
public sealed class RabbitMqSubscriptionSourceGenerator : IIncrementalGenerator
{
    internal const string SubscribeAttributeMetadataName =
        "CrestCreates.Domain.Shared.Attributes.RabbitMqSubscribeAttribute";
    internal const string TaskMetadataName = "System.Threading.Tasks.Task";
    internal const string CancellationTokenMetadataName = "System.Threading.CancellationToken";

    public static class DiagnosticDescriptors
    {
        public const string InvalidHandlerSignatureId = "CCCRMQ001";
        public const string InaccessibleDeclarationId = "CCCRMQ002";
        public const string DuplicateBindingId = "CCCRMQ003";
        public const string UnsupportedGenericShapeId = "CCCRMQ004";
        public const string InvalidAttributeArgumentId = "CCCRMQ005";

        public static readonly DiagnosticDescriptor InvalidHandlerSignature = new(
            id: InvalidHandlerSignatureId,
            title: "Invalid RabbitMQ subscription handler signature",
            messageFormat: "{0}",
            category: "CrestCreates.EventBus.RabbitMQ",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InaccessibleDeclaration = new(
            id: InaccessibleDeclarationId,
            title: "RabbitMQ subscription declaration is not accessible from generated code",
            messageFormat: "{0}",
            category: "CrestCreates.EventBus.RabbitMQ",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateBinding = new(
            id: DuplicateBindingId,
            title: "Duplicate RabbitMQ subscription binding",
            messageFormat: "{0}",
            category: "CrestCreates.EventBus.RabbitMQ",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedGenericShape = new(
            id: UnsupportedGenericShapeId,
            title: "Open generic RabbitMQ subscription shape",
            messageFormat: "{0}",
            category: "CrestCreates.EventBus.RabbitMQ",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidAttributeArgument = new(
            id: InvalidAttributeArgumentId,
            title: "Invalid RabbitMqSubscribe attribute argument",
            messageFormat: "{0}",
            category: "CrestCreates.EventBus.RabbitMQ",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var subscriptions = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (generatorContext, _) => Analyze(generatorContext))
            .Where(static analysis => analysis is not null)
            .Select(static (analysis, _) => analysis!)
            .Collect();

        context.RegisterSourceOutput(
            subscriptions.Combine(context.CompilationProvider),
            static (productionContext, source) => Emit(productionContext, source.Left, source.Right));
    }

    private static SubscriptionAnalysis? Analyze(GeneratorSyntaxContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration) is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        var attribute = methodSymbol.GetAttributes().FirstOrDefault(candidate =>
            candidate.AttributeClass is { } attributeClass &&
            attributeClass.ToDisplayString() == SubscribeAttributeMetadataName);

        if (attribute is null)
        {
            return null;
        }

        var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
            ?? declaration.GetLocation();

        var diagnostics = new List<SubscriptionDiagnostic>();
        var methodDisplay = $"{methodSymbol.ContainingType.ToDisplayString()}.{methodSymbol.Name}";

        // ── Shape validation ──
        if (methodSymbol.IsStatic)
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.InvalidHandlerSignatureId,
                $"Subscriber '{methodDisplay}' must be a concrete instance method; static handlers are not supported.",
                location));
        }

        if (methodSymbol.IsGenericMethod || methodSymbol.ContainingType.IsGenericType)
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.UnsupportedGenericShapeId,
                $"Subscriber '{methodDisplay}' must be non-generic (open generic handlers and payloads are not supported).",
                location));
        }

        if (methodSymbol.ReturnType.ToDisplayString() != TaskMetadataName)
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.InvalidHandlerSignatureId,
                $"Subscriber '{methodDisplay}' must return exactly Task (ValueTask and other shapes require " +
                "dedicated generation/run coverage and are not supported in this round).",
                location));
        }

        if (methodSymbol.Parameters.Length != 2 ||
            methodSymbol.Parameters[0].RefKind != RefKind.None ||
            methodSymbol.Parameters[1].RefKind != RefKind.None ||
            methodSymbol.Parameters[1].Type.ToDisplayString() != CancellationTokenMetadataName)
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.InvalidHandlerSignatureId,
                $"Subscriber '{methodDisplay}' must have the exact signature (TEvent payload, CancellationToken) " +
                "with no ref/out parameters.",
                location));
        }

        var payloadType = methodSymbol.Parameters.Length > 0 ? methodSymbol.Parameters[0].Type : null;
        if (payloadType is not null)
        {
            if (payloadType is IPointerTypeSymbol || payloadType.IsRefLikeType ||
                ContainsTypeParameters(payloadType))
            {
                diagnostics.Add(new SubscriptionDiagnostic(
                    DiagnosticDescriptors.InvalidHandlerSignatureId,
                    $"Subscriber '{methodDisplay}' payload type must be a closed, non-pointer, non-ref-like type.",
                    location));
            }
        }

        // ── Accessibility ──
        if (!IsAccessible(methodSymbol.ContainingType))
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.InaccessibleDeclarationId,
                $"Subscriber type '{methodSymbol.ContainingType.ToDisplayString()}' must be public or internal " +
                "(including containing types) so generated bindings in the same assembly can reference it.",
                location));
        }

        if (methodSymbol.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.InaccessibleDeclarationId,
                $"Subscriber method '{methodDisplay}' must be public or internal so generated bindings in the " +
                "same assembly can invoke it.",
                location));
        }

        if (payloadType is not null && payloadType is INamedTypeSymbol namedPayload && !IsAccessible(namedPayload))
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.InaccessibleDeclarationId,
                $"Subscriber '{methodDisplay}' payload type '{namedPayload.ToDisplayString()}' must be public or " +
                "internal (including containing types).",
                location));
        }

        if (methodSymbol.ContainingType.IsAbstract)
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.UnsupportedGenericShapeId,
                $"Subscriber type '{methodSymbol.ContainingType.ToDisplayString()}' must be a concrete " +
                "(non-abstract) class; abstract handler bases cannot be resolved from DI.",
                location));
        }

        // ── Attribute arguments ──
        string eventName = string.Empty;
        if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string nameValue)
        {
            eventName = nameValue;
        }

        if (string.IsNullOrWhiteSpace(eventName))
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.InvalidAttributeArgumentId,
                $"Subscriber '{methodDisplay}' declares an empty canonical event name.",
                location));
        }

        var version = 0;
        string? exchange = null;
        string? queue = null;
        string? routingKey = null;
        var prefetchCount = 10;

        foreach (var named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "Version":
                    if (named.Value.Value is int versionValue)
                    {
                        version = versionValue;
                    }
                    break;
                case "Exchange":
                    exchange = named.Value.Value as string;
                    break;
                case "Queue":
                    queue = named.Value.Value as string;
                    break;
                case "RoutingKey":
                    routingKey = named.Value.Value as string;
                    break;
                case "PrefetchCount":
                    if (named.Value.Value is int prefetchValue)
                    {
                        prefetchCount = prefetchValue;
                    }
                    break;
            }
        }

        if (version < 0)
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.InvalidAttributeArgumentId,
                $"Subscriber '{methodDisplay}' declares Version {version}; versions are positive integers or 0 " +
                "(unspecified, resolved at freeze).",
                location));
        }

        if (exchange is { Length: 0 })
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.InvalidAttributeArgumentId,
                $"Subscriber '{methodDisplay}' declares an empty Exchange override.",
                location));
        }

        if (queue is { Length: 0 })
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.InvalidAttributeArgumentId,
                $"Subscriber '{methodDisplay}' declares an empty Queue override.",
                location));
        }

        if (routingKey is { Length: 0 })
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.InvalidAttributeArgumentId,
                $"Subscriber '{methodDisplay}' declares an empty RoutingKey override.",
                location));
        }

        if (prefetchCount is < 1 or > ushort.MaxValue)
        {
            diagnostics.Add(new SubscriptionDiagnostic(
                DiagnosticDescriptors.InvalidAttributeArgumentId,
                $"Subscriber '{methodDisplay}' declares PrefetchCount {prefetchCount}; the valid range is " +
                $"1..{ushort.MaxValue}.",
                location));
        }

        return new SubscriptionAnalysis(
            methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            methodSymbol.Name,
            payloadType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "object",
            eventName,
            version,
            exchange,
            queue,
            routingKey,
            prefetchCount,
            location,
            diagnostics.Count > 0,
            diagnostics);
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<SubscriptionAnalysis> analyses, Compilation compilation)
    {
        if (analyses.IsDefaultOrEmpty)
        {
            return;
        }

        if (compilation.AssemblyName is not { Length: > 0 } assemblyName)
        {
            return;
        }

        var ordered = analyses
            .Where(analysis => analysis is not null)
            .OrderBy(analysis => analysis.HandlerTypeDisplay, StringComparer.Ordinal)
            .ThenBy(analysis => analysis.HandlerMethod, StringComparer.Ordinal)
            .ToList();

        foreach (var analysis in ordered)
        {
            foreach (var diagnostic in analysis.Diagnostics)
            {
                context.ReportDiagnostic(ToDiagnostic(diagnostic));
            }
        }

        var valid = ordered.Where(analysis => !analysis.HasErrors).ToList();
        if (valid.Count == 0)
        {
            return;
        }

        var duplicates = valid
            .GroupBy(a => (a.HandlerTypeDisplay, a.HandlerMethod, a.EventName, a.Version,
                a.Exchange ?? string.Empty, a.Queue ?? string.Empty, a.RoutingKey ?? string.Empty))
            .Where(group => group.Count() > 1)
            .ToList();

        foreach (var duplicate in duplicates)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.DuplicateBinding,
                duplicate.First().Location,
                $"Handler '{duplicate.Key.HandlerTypeDisplay}.{duplicate.Key.HandlerMethod}' declares a duplicate " +
                $"subscription binding for '{duplicate.Key.EventName}' v{duplicate.Key.Version}."));
        }

        if (duplicates.Count > 0)
        {
            return;
        }

        var source = RabbitMqSubscriptionCodeWriter.Write(assemblyName, valid);
        context.AddSource("RabbitMqSubscriptionRegistration.g.cs", SourceText.From(source, System.Text.Encoding.UTF8));
    }

    private static Diagnostic ToDiagnostic(SubscriptionDiagnostic diagnostic)
    {
        var descriptor = diagnostic.Id switch
        {
            DiagnosticDescriptors.InaccessibleDeclarationId => DiagnosticDescriptors.InaccessibleDeclaration,
            DiagnosticDescriptors.DuplicateBindingId => DiagnosticDescriptors.DuplicateBinding,
            DiagnosticDescriptors.UnsupportedGenericShapeId => DiagnosticDescriptors.UnsupportedGenericShape,
            DiagnosticDescriptors.InvalidAttributeArgumentId => DiagnosticDescriptors.InvalidAttributeArgument,
            _ => DiagnosticDescriptors.InvalidHandlerSignature,
        };

        return Diagnostic.Create(descriptor, diagnostic.Location, diagnostic.Message);
    }

    private static bool IsAccessible(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsTypeParameters(ITypeSymbol type)
        => type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => ContainsTypeParameters(array.ElementType),
            INamedTypeSymbol named when named.IsGenericType &&
                named.TypeArguments.Any(ContainsTypeParameters) => true,
            _ => false,
        };

    internal sealed record SubscriptionAnalysis(
        string HandlerTypeDisplay,
        string HandlerMethod,
        string PayloadTypeDisplay,
        string EventName,
        int Version,
        string? Exchange,
        string? Queue,
        string? RoutingKey,
        int PrefetchCount,
        Location Location,
        bool HasErrors,
        IReadOnlyList<SubscriptionDiagnostic> Diagnostics);

    internal sealed record SubscriptionDiagnostic(string Id, string Message, Location Location);
}
