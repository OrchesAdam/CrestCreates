using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.EventDescriptorGenerator;

/// <summary>
/// Generates the per-assembly event descriptor contribution for [CrestEvent] declarations: a static
/// IEventDescriptorProvider plus a public AddEventDescriptors() extension that the host (or the owning
/// framework package) must call explicitly. Integration-scope declarations additionally require the
/// JsonContracts toolchain so their wire codec contribution is available; there is no runtime scanning
/// and no fallback when the contributor is not activated.
/// </summary>
[Generator]
public sealed class EventDescriptorSourceGenerator : IIncrementalGenerator
{
    internal const string CrestEventAttributeMetadataName = "CrestCreates.Event.Abstractions.CrestEventAttribute";
    internal const string ToolchainPropertyName = "build_property.CrestCreatesEventJsonContracts";

    public static class DiagnosticDescriptors
    {
        public const string IntegrationMissingCodecToolchainId = "CCCEV001";
        public const string InvalidDeclarationId = "CCCEV002";
        public const string DuplicateIdentityId = "CCCEV003";

        public static readonly DiagnosticDescriptor IntegrationMissingCodecToolchain = new(
            id: IntegrationMissingCodecToolchainId,
            title: "Integration event requires the JSON contract toolchain",
            messageFormat: "{0}",
            category: "CrestCreates.Event",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidDeclaration = new(
            id: InvalidDeclarationId,
            title: "Invalid [CrestEvent] declaration",
            messageFormat: "{0}",
            category: "CrestCreates.Event",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateIdentity = new(
            id: DuplicateIdentityId,
            title: "Duplicate canonical event identity in this assembly",
            messageFormat: "{0}",
            category: "CrestCreates.Event",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (generatorContext, _) => Analyze(generatorContext))
            .Where(static analysis => analysis is not null)
            .Select(static (analysis, _) => analysis!)
            .Collect();

        var compilationAndOptions = context.CompilationProvider.Combine(context.AnalyzerConfigOptionsProvider);

        context.RegisterSourceOutput(
            candidates.Combine(compilationAndOptions),
            static (productionContext, source) =>
                Emit(productionContext, source.Left, source.Right));
    }

    private static EventDeclaration? Analyze(GeneratorSyntaxContext context)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol typeSymbol)
        {
            return null;
        }

        var attribute = typeSymbol.GetAttributes()
            .FirstOrDefault(candidate =>
                candidate.AttributeClass is { } attributeClass &&
                attributeClass.ToDisplayString() == CrestEventAttributeMetadataName);

        if (attribute is null)
        {
            return null;
        }

        var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
            ?? declaration.GetLocation();

        var diagnostics = new List<EventDeclarationDiagnostic>();
        var typeDisplay = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (typeSymbol.TypeKind != TypeKind.Class || typeSymbol.IsAbstract || typeSymbol.IsGenericType)
        {
            diagnostics.Add(new EventDeclarationDiagnostic(
                DiagnosticDescriptors.InvalidDeclarationId,
                $"Event '{typeDisplay}' must be a closed, non-abstract class; " +
                "open generics, abstract classes and other shapes have no closed payload contract.",
                location));
        }
        else if (!IsAccessible(typeSymbol))
        {
            diagnostics.Add(new EventDeclarationDiagnostic(
                DiagnosticDescriptors.InvalidDeclarationId,
                $"Event '{typeDisplay}' must be public or internal (including containing types) so generated " +
                "descriptors and wire codecs in the same assembly can reference it.",
                location));
        }

        string? name = null;
        EventScopeValue? scope = null;
        var version = 1;
        string? explicitId = null;
        var reliability = "AtLeastOnce";
        string? importance = null;
        var requiresIdempotency = false;
        var isAuditable = false;
        var isReplayable = false;
        var isPublic = false;
        string? description = null;
        string? capabilityId = null;

        foreach (var named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "Name":
                    name = named.Value.Value as string;
                    break;
                case "Scope":
                    scope = ReadScope(named.Value, attribute);
                    break;
                case "Version":
                    if (named.Value.Value is int versionValue)
                    {
                        version = versionValue;
                    }
                    break;
                case "Id":
                    explicitId = named.Value.Value as string;
                    break;
                case "Reliability":
                    reliability = ReadEnumMemberName(named.Value, "AtLeastOnce");
                    break;
                case "Importance":
                    importance = ReadEnumMemberName(named.Value, "Critical");
                    break;
                case "RequiresIdempotency":
                    requiresIdempotency = named.Value.Value is true;
                    break;
                case "IsAuditable":
                    isAuditable = named.Value.Value is true;
                    break;
                case "IsReplayable":
                    isReplayable = named.Value.Value is true;
                    break;
                case "IsPublic":
                    isPublic = named.Value.Value is true;
                    break;
                case "Description":
                    description = named.Value.Value as string;
                    break;
                case "CapabilityId":
                    capabilityId = named.Value.Value as string;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            diagnostics.Add(new EventDeclarationDiagnostic(
                DiagnosticDescriptors.InvalidDeclarationId,
                $"Event '{typeDisplay}' must declare a non-empty canonical Name via [CrestEvent(Name = ...)].",
                location));
        }

        if (scope is null)
        {
            diagnostics.Add(new EventDeclarationDiagnostic(
                DiagnosticDescriptors.InvalidDeclarationId,
                $"Event '{typeDisplay}' must explicitly declare Scope via [CrestEvent(Scope = EventScope...)]; " +
                "the implicit default is not accepted for canonical declarations.",
                location));
        }

        if (version < 1)
        {
            diagnostics.Add(new EventDeclarationDiagnostic(
                DiagnosticDescriptors.InvalidDeclarationId,
                $"Event '{typeDisplay}' declares Version {version}; canonical event versions start at 1.",
                location));
        }

        if (explicitId is not null && string.IsNullOrWhiteSpace(explicitId))
        {
            diagnostics.Add(new EventDeclarationDiagnostic(
                DiagnosticDescriptors.InvalidDeclarationId,
                $"Event '{typeDisplay}' declares an empty explicit Id.",
                location));
        }

        if (diagnostics.Count > 0 || name is null || scope is null)
        {
            return new EventDeclaration(typeDisplay, name ?? string.Empty, version, scope ?? EventScopeValue.Local,
                explicitId, reliability, importance, requiresIdempotency, isAuditable, isReplayable, isPublic,
                description, capabilityId, location, true, diagnostics);
        }

        return new EventDeclaration(typeDisplay, name, version, scope.Value,
            explicitId, reliability, importance, requiresIdempotency, isAuditable, isReplayable, isPublic,
            description, capabilityId, location, false, diagnostics);
    }

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<EventDeclaration> declarations,
        (Compilation Compilation, AnalyzerConfigOptionsProvider Options) environment)
    {
        var (compilation, optionsProvider) = environment;

        if (declarations.IsDefaultOrEmpty)
        {
            return;
        }

        // Partial declarations of the same type surface as multiple candidates; the display name is
        // unique per type symbol, so the first (ordinal) analysis wins deterministically.
        var ordered = declarations
            .Where(declaration => declaration is not null)
            .GroupBy(declaration => declaration.TypeDisplayName)
            .Select(group => group.First())
            .OrderBy(declaration => declaration.TypeDisplayName, StringComparer.Ordinal)
            .ToList();

        foreach (var declaration in ordered)
        {
            foreach (var diagnostic in declaration.Diagnostics)
            {
                context.ReportDiagnostic(ToDiagnostic(diagnostic));
            }
        }

        var valid = ordered.Where(d => !d.HasErrors).ToList();
        if (valid.Count == 0)
        {
            return;
        }

        if (compilation.AssemblyName is not { Length: > 0 } assemblyName)
        {
            return;
        }

        // Duplicate canonical identity within this assembly (name+version or explicit id).
        var duplicateDiagnostics = new List<EventDeclarationDiagnostic>();
        foreach (var group in valid.GroupBy(d => (d.Name, d.Version)))
        {
            if (group.Count() > 1)
            {
                duplicateDiagnostics.Add(new EventDeclarationDiagnostic(
                    DiagnosticDescriptors.DuplicateIdentityId,
                    $"Canonical event identity '{group.Key.Name}' v{group.Key.Version} is declared by multiple " +
                    $"types in this assembly: {string.Join(", ", group.Select(g => g.TypeDisplayName))}.",
                    group.First().Location));
            }
        }

        foreach (var group in valid.Where(d => d.ExplicitId is not null).GroupBy(d => d.ExplicitId!))
        {
            if (group.Count() > 1)
            {
                duplicateDiagnostics.Add(new EventDeclarationDiagnostic(
                    DiagnosticDescriptors.DuplicateIdentityId,
                    $"Explicit event Id '{group.Key}' is declared by multiple types in this assembly: " +
                    $"{string.Join(", ", group.Select(g => g.TypeDisplayName))}.",
                    group.First().Location));
            }
        }

        foreach (var diagnostic in duplicateDiagnostics)
        {
            context.ReportDiagnostic(ToDiagnostic(diagnostic));
        }

        var hasToolchain = optionsProvider.GlobalOptions.TryGetValue(ToolchainPropertyName, out var toolchainValue) &&
                           string.Equals(toolchainValue, "true", StringComparison.OrdinalIgnoreCase);

        var hasIntegration = false;
        foreach (var declaration in valid)
        {
            if (declaration.Scope != EventScopeValue.Integration)
            {
                continue;
            }

            hasIntegration = true;
            if (!hasToolchain)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.IntegrationMissingCodecToolchain,
                    declaration.Location,
                    $"Integration event '{declaration.Name}' requires a source-generated JSON codec. Import the " +
                    "CrestCreates.JsonContracts.Build toolchain in this project so its generated roots and codec " +
                    "contribution are available."));
            }
        }

        var source = EventDescriptorCodeWriter.Write(
            assemblyName,
            valid,
            emitCodecContribution: hasToolchain && hasIntegration && duplicateDiagnostics.Count == 0);

        context.AddSource("EventDescriptorRegistration.g.cs", SourceText.From(source, System.Text.Encoding.UTF8));
    }

    private static Diagnostic ToDiagnostic(EventDeclarationDiagnostic diagnostic)
    {
        var descriptor = diagnostic.Id switch
        {
            DiagnosticDescriptors.IntegrationMissingCodecToolchainId =>
                DiagnosticDescriptors.IntegrationMissingCodecToolchain,
            DiagnosticDescriptors.DuplicateIdentityId => DiagnosticDescriptors.DuplicateIdentity,
            _ => DiagnosticDescriptors.InvalidDeclaration,
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

    private static EventScopeValue? ReadScope(TypedConstant constant, AttributeData attribute)
    {
        if (constant.Value is not int value)
        {
            return null;
        }

        var enumType = attribute.AttributeConstructor?.Parameters
            .FirstOrDefault(parameter => parameter.Name == "Scope")?.Type;
        if (enumType is not INamedTypeSymbol namedEnum)
        {
            return value switch { 0 => EventScopeValue.Local, 1 => EventScopeValue.Domain, 2 => EventScopeValue.Integration, _ => null };
        }

        var member = namedEnum.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field => field.ConstantValue is int memberValue && memberValue == value);
        if (member is null)
        {
            return null;
        }

        return member.Name switch
        {
            "Local" => EventScopeValue.Local,
            "Domain" => EventScopeValue.Domain,
            "Integration" => EventScopeValue.Integration,
            _ => null,
        };
    }

    private static string ReadEnumMemberName(TypedConstant constant, string fallback)
    {
        if (constant.Type is not INamedTypeSymbol enumType || constant.Value is not int value)
        {
            return fallback;
        }

        var member = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field => field.ConstantValue is int memberValue && memberValue == value);

        return member?.Name ?? fallback;
    }

    internal sealed record EventDeclaration(
        string TypeDisplayName,
        string Name,
        int Version,
        EventScopeValue Scope,
        string? ExplicitId,
        string Reliability,
        string? Importance,
        bool RequiresIdempotency,
        bool IsAuditable,
        bool IsReplayable,
        bool IsPublic,
        string? Description,
        string? CapabilityId,
        Location Location,
        bool HasErrors,
        IReadOnlyList<EventDeclarationDiagnostic> Diagnostics);

    internal sealed record EventDeclarationDiagnostic(string Id, string Message, Location Location);

    internal enum EventScopeValue { Local, Domain, Integration }
}
