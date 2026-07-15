using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.CodeGenerator.McpToolGenerator;

[Generator]
public sealed class McpToolGenerator : IIncrementalGenerator
{
    private const string ContainerAttribute = "CrestCreates.Mcp.McpToolSpecsAttribute";
    private const string SpecAttribute = "CrestCreates.Mcp.McpToolSpecAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var containers = context.SyntaxProvider.ForAttributeWithMetadataName(
            ContainerAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => Normalize(ctx));

        context.RegisterSourceOutput(containers, static (spc, container) =>
        {
            foreach (var diagnostic in container.Diagnostics)
                spc.ReportDiagnostic(diagnostic);

            if (container.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                return;

            spc.AddSource(
                HintName(container, "Provider"),
                McpToolProviderEmitter.Emit(container));
            spc.AddSource(
                HintName(container, "Bindings"),
                McpToolBindingEmitter.Emit(container));
        });
    }

    private static string HintName(McpToolContainerModel container, string suffix)
    {
        var identity = string.IsNullOrEmpty(container.Namespace)
            ? container.Name
            : container.Namespace + "." + container.Name;
        var sanitized = Regex.Replace(identity, "[^A-Za-z0-9_.-]", "_");
        return sanitized + "_McpTool" + suffix + ".g.cs";
    }

    private static McpToolContainerModel Normalize(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (ClassDeclarationSyntax)context.TargetNode;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var specs = ImmutableArray.CreateBuilder<McpToolSpecModel>();

        if (!symbol.IsStatic
            || symbol.TypeParameters.Length != 0
            || symbol.ContainingType is not null
            || !syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Diagnostic.Create(McpToolDiagnostics.InvalidContainer, syntax.Identifier.GetLocation()));
        }

        foreach (var nested in symbol.GetTypeMembers())
        {
            var attribute = nested.GetAttributes().FirstOrDefault(item =>
                item.AttributeClass?.ToDisplayString() == SpecAttribute);
            if (attribute is null)
                continue;

            var location = nested.Locations.FirstOrDefault() ?? syntax.Identifier.GetLocation();
            if (nested.ContainingType is null
                || !SymbolEqualityComparer.Default.Equals(nested.ContainingType, symbol)
                || nested.TypeKind != TypeKind.Class
                || nested.IsStatic
                || nested.TypeParameters.Length != 0)
            {
                diagnostics.Add(Diagnostic.Create(McpToolDiagnostics.InvalidSpec, location));
                continue;
            }

            var capabilityId = attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as string ?? string.Empty
                : string.Empty;
            var descriptorId = GetString(attribute, "DescriptorId") ?? "mcp-tool:" + capabilityId;
            var descriptorVersion = GetInt(attribute, "DescriptorVersion", 1);
            var capabilityVersion = GetInt(attribute, "CapabilityVersion", 0);
            var toolName = GetString(attribute, "ToolName") ?? capabilityId;
            var description = GetString(attribute, "Description") ?? string.Empty;
            var input = GetType(attribute, "InputType");
            var output = GetType(attribute, "OutputType");

            if (string.IsNullOrWhiteSpace(capabilityId))
                diagnostics.Add(Diagnostic.Create(McpToolDiagnostics.InvalidSpec, location));
            if (descriptorVersion <= 0)
                diagnostics.Add(Diagnostic.Create(McpToolDiagnostics.InvalidDescriptorVersion, location));
            if (capabilityVersion < 0)
                diagnostics.Add(Diagnostic.Create(McpToolDiagnostics.NegativeCapabilityVersion, location));
            if (!Regex.IsMatch(toolName, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant))
                diagnostics.Add(Diagnostic.Create(McpToolDiagnostics.InvalidToolName, location));
            if (string.IsNullOrWhiteSpace(description))
                diagnostics.Add(Diagnostic.Create(McpToolDiagnostics.EmptyDescription, location));
            if (!IsSupportedDto(input) || !IsSupportedDto(output))
                diagnostics.Add(Diagnostic.Create(McpToolDiagnostics.UnsupportedType, location));

            var destructiveHint = GetInt(attribute, "DestructiveHint", 0);
            var idempotentHint = GetInt(attribute, "IdempotentHint", 0);
            var openWorldHint = GetInt(attribute, "OpenWorldHint", 0);
            if (!IsValidHint(destructiveHint) || !IsValidHint(idempotentHint) || !IsValidHint(openWorldHint))
                diagnostics.Add(Diagnostic.Create(McpToolDiagnostics.InvalidSpec, location));

            specs.Add(new McpToolSpecModel
            {
                SpecName = nested.Name,
                CapabilityId = capabilityId,
                CapabilityVersion = capabilityVersion,
                DescriptorId = descriptorId,
                DescriptorVersion = descriptorVersion,
                ToolName = toolName,
                Title = GetString(attribute, "Title"),
                Description = description,
                InputType = input?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                OutputType = output?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                DestructiveHint = destructiveHint,
                IdempotentHint = idempotentHint,
                OpenWorldHint = openWorldHint
            });
        }

        foreach (var group in specs.GroupBy(spec => spec.ToolName, StringComparer.Ordinal).Where(group => group.Count() > 1))
            diagnostics.Add(Diagnostic.Create(McpToolDiagnostics.DuplicateIdentity, syntax.Identifier.GetLocation()));
        foreach (var group in specs.GroupBy(spec => (spec.DescriptorId, spec.DescriptorVersion)).Where(group => group.Count() > 1))
            diagnostics.Add(Diagnostic.Create(McpToolDiagnostics.DuplicateIdentity, syntax.Identifier.GetLocation()));

        return new McpToolContainerModel
        {
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString(),
            Name = symbol.Name,
            Specs = specs.ToImmutable(),
            Diagnostics = diagnostics.ToImmutable()
        };
    }

    private static bool IsSupportedDto(ITypeSymbol? type)
    {
        if (type is null)
            return true;
        if (type is not INamedTypeSymbol named || named.IsUnboundGenericType || named.TypeArguments.Any(item => item.TypeKind == TypeKind.TypeParameter))
            return false;
        if (named.TypeKind is TypeKind.Interface or TypeKind.Dynamic or TypeKind.Enum or TypeKind.Delegate
            || named.IsAbstract)
            return false;
        var fullyQualifiedName = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (fullyQualifiedName is "global::System.DateTime"
            or "global::System.DateTimeOffset"
            or "global::System.DateOnly"
            or "global::System.TimeOnly"
            or "global::System.Guid")
            return false;
        if (named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
            && named.Name is "List"
                or "HashSet"
                or "IList"
                or "ICollection"
                or "IEnumerable"
                or "IReadOnlyList"
                or "IReadOnlyCollection")
            return false;
        if (named.TypeKind == TypeKind.Struct
            && !named.GetMembers().OfType<IPropertySymbol>().Any(property => !property.IsStatic))
            return false;
        var original = named.OriginalDefinition.ToDisplayString();
        return original != "System.Collections.Generic.Dictionary<TKey, TValue>"
            && type.SpecialType == SpecialType.None;
    }

    private static string? GetString(AttributeData attribute, string name)
        => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;

    private static int GetInt(AttributeData attribute, string name, int fallback)
    {
        var value = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value;
        return value is int number ? number : fallback;
    }

    private static ITypeSymbol? GetType(AttributeData attribute, string name)
        => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as ITypeSymbol;

    private static bool IsValidHint(int value) => value >= 0 && value <= 2;
}
