using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.CodeGenerator.LocalEventGenerator;

/// <summary>
/// Semantic analysis for local event handler bindings and [LocalEvent] declarations. Discovery is
/// based on the closed ILocalEventHandler&lt;TEvent&gt; interface, never on type-name matching.
/// </summary>
internal static class LocalEventSymbolAnalyzer
{
    public static LocalEventAnalysis? Analyze(GeneratorSyntaxContext context)
    {
        if (context.Node is not TypeDeclarationSyntax declaration)
        {
            return null;
        }

        if (context.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        var compilation = context.SemanticModel.Compilation;
        var handlerInterface = compilation.GetTypeByMetadataName(LocalEventSourceGenerator.HandlerInterfaceMetadataName);
        var localEventInterface = compilation.GetTypeByMetadataName(LocalEventSourceGenerator.LocalEventInterfaceMetadataName);
        if (handlerInterface is null || localEventInterface is null)
        {
            return null;
        }

        var handlers = ImmutableArray.CreateBuilder<HandlerBindingInfo>();
        var eventDeclarations = ImmutableArray.CreateBuilder<EventDeclarationInfo>();
        var diagnostics = ImmutableArray.CreateBuilder<LocalEventDiagnostic>();

        AnalyzeHandlerInterfaces(symbol, declaration, handlerInterface, localEventInterface, handlers, eventDeclarations, diagnostics);
        AnalyzeOwnDeclaration(symbol, declaration, localEventInterface, eventDeclarations, diagnostics);

        if (handlers.Count == 0 && eventDeclarations.Count == 0 && diagnostics.Count == 0)
        {
            return null;
        }

        return new LocalEventAnalysis(handlers.ToImmutable(), eventDeclarations.ToImmutable(), diagnostics.ToImmutable());
    }

    private static void AnalyzeHandlerInterfaces(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        INamedTypeSymbol handlerInterface,
        INamedTypeSymbol localEventInterface,
        ImmutableArray<HandlerBindingInfo>.Builder handlers,
        ImmutableArray<EventDeclarationInfo>.Builder eventDeclarations,
        ImmutableArray<LocalEventDiagnostic>.Builder diagnostics)
    {
        var closedInterfaces = symbol.AllInterfaces
            .Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, handlerInterface))
            .ToList();

        if (closedInterfaces.Count == 0)
        {
            return;
        }

        if (symbol.IsGenericType)
        {
            diagnostics.Add(new LocalEventDiagnostic(
                LocalEventSourceGenerator.DiagnosticDescriptors.OpenGenericId,
                $"Handler type '{symbol.ToDisplayString()}' is an open generic; local event handler registration requires a closed, non-generic handler class.",
                declaration.Identifier.GetLocation()));
            return;
        }

        if (symbol.IsAbstract)
        {
            // Abstract bases intentionally implement the interface for derived handlers; the
            // derived closed types are discovered on their own. No binding, no diagnostic.
            return;
        }

        if (!IsAccessibleFromGeneratedCode(symbol.DeclaredAccessibility))
        {
            diagnostics.Add(new LocalEventDiagnostic(
                LocalEventSourceGenerator.DiagnosticDescriptors.UnsupportedAccessibilityId,
                $"Handler type '{symbol.ToDisplayString()}' is not accessible from generated registration code; use public or internal accessibility.",
                declaration.Identifier.GetLocation()));
            return;
        }

        foreach (var closedInterface in closedInterfaces)
        {
            var eventType = closedInterface.TypeArguments[0];

            if (eventType is ITypeParameterSymbol || eventType.TypeKind == TypeKind.Error)
            {
                diagnostics.Add(new LocalEventDiagnostic(
                    LocalEventSourceGenerator.DiagnosticDescriptors.OpenGenericId,
                    $"Handler '{symbol.ToDisplayString()}' closes ILocalEventHandler over an open or unresolved event type; a concrete event type is required.",
                    declaration.Identifier.GetLocation()));
                continue;
            }

            if (eventType is not INamedTypeSymbol namedEventType)
            {
                diagnostics.Add(new LocalEventDiagnostic(
                    LocalEventSourceGenerator.DiagnosticDescriptors.OpenGenericId,
                    $"Handler '{symbol.ToDisplayString()}' closes ILocalEventHandler over an unsupported event shape; a named concrete event type is required.",
                    declaration.Identifier.GetLocation()));
                continue;
            }

            if (!ImplementsLocalEvent(namedEventType, localEventInterface))
            {
                continue;
            }

            if (!IsAccessibleFromGeneratedCode(namedEventType.DeclaredAccessibility))
            {
                diagnostics.Add(new LocalEventDiagnostic(
                    LocalEventSourceGenerator.DiagnosticDescriptors.UnsupportedAccessibilityId,
                    $"Event type '{namedEventType.ToDisplayString()}' is not accessible from generated registration code; use public or internal accessibility.",
                    declaration.Identifier.GetLocation()));
                continue;
            }

            handlers.Add(new HandlerBindingInfo(
                HandlerGlobalName: GlobalName(symbol),
                HandlerMetadataName: symbol.ToDisplayString(),
                EventGlobalName: GlobalName(namedEventType),
                EventMetadataName: namedEventType.ToDisplayString(),
                Location: declaration.Identifier.GetLocation()));

            // Codec ownership follows the assembly that declares the event, not the assembly that
            // merely references it through a handler. A handler-only assembly still contributes the
            // typed binding and its event metadata, but must never be treated as the codec owner.
            var eventIsOwned = SymbolEqualityComparer.Default.Equals(
                namedEventType.ContainingAssembly, symbol.ContainingAssembly);
            var declarationInfo = ReadEventDeclaration(namedEventType, eventIsOwned, diagnostics);
            if (declarationInfo is not null)
            {
                eventDeclarations.Add(declarationInfo);
            }
        }
    }

    private static void AnalyzeOwnDeclaration(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        INamedTypeSymbol localEventInterface,
        ImmutableArray<EventDeclarationInfo>.Builder eventDeclarations,
        ImmutableArray<LocalEventDiagnostic>.Builder diagnostics)
    {
        if (!HasAttribute(symbol, LocalEventSourceGenerator.LocalEventAttributeMetadataName))
        {
            return;
        }

        if (!ImplementsLocalEvent(symbol, localEventInterface))
        {
            diagnostics.Add(new LocalEventDiagnostic(
                LocalEventSourceGenerator.DiagnosticDescriptors.InvalidEventTypeId,
                $"Type '{symbol.ToDisplayString()}' is marked [LocalEvent] but does not implement ILocalEvent.",
                declaration.Identifier.GetLocation()));
            return;
        }

        var info = ReadEventDeclaration(symbol, isOwned: true, diagnostics);
        if (info is not null)
        {
            eventDeclarations.Add(info);
        }
    }

    private static EventDeclarationInfo? ReadEventDeclaration(
        INamedTypeSymbol eventSymbol,
        bool isOwned,
        ImmutableArray<LocalEventDiagnostic>.Builder diagnostics)
    {
        var attribute = eventSymbol.GetAttributes()
            .FirstOrDefault(candidate => candidate.AttributeClass?.ToDisplayString() == LocalEventSourceGenerator.LocalEventAttributeMetadataName);

        if (attribute is null)
        {
            return null;
        }

        var replayable = false;
        var version = 1;
        string? replayKey = null;
        var governanceRequired = false;
        var tenantMode = "Optional";
        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key == "Replayable" && named.Value.Value is bool replayableValue)
            {
                replayable = replayableValue;
            }
            else if (named.Key == "Version" && named.Value.Value is int versionValue)
            {
                version = versionValue;
            }
            else if (named.Key == "ReplayKey" && named.Value.Value is string replayKeyValue)
            {
                replayKey = replayKeyValue;
            }
            else if (named.Key == "Governance" && named.Value.Value is int governanceValue)
            {
                governanceRequired = governanceValue == 1;
            }
            else if (named.Key == "TenantMode" && named.Value.Value is int tenantModeValue)
            {
                tenantMode = tenantModeValue switch
                {
                    1 => "Required",
                    2 => "HostOnly",
                    _ => "Optional",
                };
            }
        }

        var replayAliases = ImmutableArray.CreateBuilder<ReplayAliasInfo>();
        foreach (var aliasAttribute in eventSymbol.GetAttributes())
        {
            if (aliasAttribute.AttributeClass?.ToDisplayString() != LocalEventSourceGenerator.ReplayAliasAttributeMetadataName)
            {
                continue;
            }

            if (aliasAttribute.ConstructorArguments.Length == 0 ||
                aliasAttribute.ConstructorArguments[0].Value is not string aliasKey)
            {
                continue;
            }

            var aliasVersion = aliasAttribute.ConstructorArguments.Length > 1 &&
                               aliasAttribute.ConstructorArguments[1].Value is int parsedAliasVersion
                ? parsedAliasVersion
                : 1;
            replayAliases.Add(new ReplayAliasInfo(aliasKey, aliasVersion));
        }

        var legacyIdentities = ImmutableArray.CreateBuilder<LegacyIdentityInfo>();
        foreach (var legacyAttribute in eventSymbol.GetAttributes())
        {
            if (legacyAttribute.AttributeClass?.ToDisplayString() != LocalEventSourceGenerator.LegacyIdentityAttributeMetadataName)
            {
                continue;
            }

            if (legacyAttribute.ConstructorArguments.Length == 0 ||
                legacyAttribute.ConstructorArguments[0].Value is not string legacyName)
            {
                continue;
            }

            var legacyVersion = legacyAttribute.ConstructorArguments.Length > 1 &&
                                legacyAttribute.ConstructorArguments[1].Value is int parsedVersion
                ? parsedVersion
                : 1;
            legacyIdentities.Add(new LegacyIdentityInfo(legacyName, legacyVersion));
        }

        var location = eventSymbol.Locations.FirstOrDefault();
        if (isOwned && version < 1)
        {
            diagnostics.Add(new LocalEventDiagnostic(
                LocalEventSourceGenerator.DiagnosticDescriptors.InvalidEventTypeId,
                $"Event '{eventSymbol.ToDisplayString()}' declares invalid version v{version}; versions must be >= 1.",
                location));
        }

        if (isOwned && replayable && eventSymbol.IsAbstract)
        {
            diagnostics.Add(new LocalEventDiagnostic(
                LocalEventSourceGenerator.DiagnosticDescriptors.UnsealedPayloadId,
                $"Event '{eventSymbol.ToDisplayString()}' is abstract and cannot be declared replayable; the JSON payload contract must be a closed type.",
                location));
        }

        if (isOwned && replayable && eventSymbol.IsGenericType)
        {
            diagnostics.Add(new LocalEventDiagnostic(
                LocalEventSourceGenerator.DiagnosticDescriptors.UnsealedPayloadId,
                $"Event '{eventSymbol.ToDisplayString()}' is an open generic and cannot be declared replayable.",
                location));
        }

        return new EventDeclarationInfo(
            EventMetadataName: eventSymbol.ToDisplayString(),
            Replayable: replayable,
            Version: version,
            ReplayKey: replayKey,
            GovernanceRequired: governanceRequired,
            TenantMode: tenantMode,
            ReplayAliases: replayAliases.ToImmutable(),
            LegacyIdentities: legacyIdentities.ToImmutable(),
            Location: location,
            Owned: isOwned);
    }

    private static bool ImplementsLocalEvent(ITypeSymbol type, INamedTypeSymbol localEventInterface)
    {
        if (SymbolEqualityComparer.Default.Equals(type, localEventInterface))
        {
            return true;
        }

        return type.AllInterfaces.Any(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate, localEventInterface));
    }

    private static bool HasAttribute(INamedTypeSymbol symbol, string metadataName)
        => symbol.GetAttributes().Any(candidate => candidate.AttributeClass?.ToDisplayString() == metadataName);

    private static bool IsAccessibleFromGeneratedCode(Accessibility accessibility)
        => accessibility is Accessibility.Public
            or Accessibility.Internal
            or Accessibility.ProtectedOrInternal;

    private static string GlobalName(ITypeSymbol symbol)
        => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
