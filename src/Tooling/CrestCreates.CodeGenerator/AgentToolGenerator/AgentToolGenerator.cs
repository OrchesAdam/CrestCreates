using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.CodeGenerator.AgentToolGenerator;

[Generator]
public sealed class AgentToolGenerator : IIncrementalGenerator
{
    private const string ContainerAttribute = "CrestCreates.Agent.Tools.AgentToolSpecsAttribute";
    private const string SpecAttribute = "CrestCreates.Agent.Tools.AgentToolSpecAttribute";
    private const string ToolNamePattern = "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$";

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

            if (container.Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                return;

            spc.AddSource(HintName(container, "Provider"), AgentToolProviderEmitter.Emit(container));
            spc.AddSource(HintName(container, "Bindings"), AgentToolBindingEmitter.Emit(container));
        });
    }

    private static AgentToolContainerModel Normalize(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (ClassDeclarationSyntax)context.TargetNode;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var specs = ImmutableArray.CreateBuilder<AgentToolSpecModel>();

        if (!symbol.IsStatic
            || symbol.TypeParameters.Length != 0
            || symbol.ContainingType is not null
            || !syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.InvalidContainer, syntax.Identifier.GetLocation()));
        }

        foreach (var nested in EnumerateNestedTypes(symbol))
        {
            var attribute = nested.GetAttributes().FirstOrDefault(static item =>
                item.AttributeClass?.ToDisplayString() == SpecAttribute);
            if (attribute is null)
                continue;

            var location = nested.Locations.FirstOrDefault() ?? syntax.Identifier.GetLocation();
            if (!SymbolEqualityComparer.Default.Equals(nested.ContainingType, symbol)
                || nested.TypeKind != TypeKind.Class
                || nested.IsStatic
                || nested.TypeParameters.Length != 0)
            {
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.InvalidSpec, location));
                continue;
            }

            var capabilityId = attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as string ?? string.Empty
                : string.Empty;
            var descriptorId = GetString(attribute, "DescriptorId") ?? "agent-tool:" + capabilityId;
            var descriptorVersion = GetInt(attribute, "DescriptorVersion", 1);
            var capabilityVersion = GetInt(attribute, "CapabilityVersion", 0);
            var toolName = GetString(attribute, "ToolName") ?? capabilityId;
            var title = GetString(attribute, "Title");
            var description = GetString(attribute, "Description") ?? string.Empty;
            var budgetCategory = GetString(attribute, "BudgetCategory") ?? string.Empty;
            var input = GetType(attribute, "InputType");
            var output = GetType(attribute, "OutputType");
            var selectionPolicy = GetInt(attribute, "SelectionPolicy", 1);
            var sideEffectKind = GetInt(attribute, "SideEffectKind", 0);
            var riskFloor = GetInt(attribute, "RiskFloor", 0);
            var approvalMode = GetInt(attribute, "ApprovalMode", 1);
            var costUnits = GetLong(attribute, "CostUnits", 1);
            var maxCallsPerExecution = GetInt(attribute, "MaxCallsPerExecution", 0);
            var auditMode = GetInt(attribute, "AuditMode", 1);
            var roles = GetStringArray(attribute, "AllowedAgentRoles")
                .OrderBy(static role => role, StringComparer.Ordinal)
                .ToImmutableArray();

            if (string.IsNullOrWhiteSpace(capabilityId))
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.InvalidCapabilityId, location));
            if (!Regex.IsMatch(toolName, ToolNamePattern, RegexOptions.CultureInvariant))
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.InvalidToolName, location));
            if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(budgetCategory))
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.MissingRequiredText, location));
            if (descriptorVersion <= 0)
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.InvalidDescriptorVersion, location));
            if (string.IsNullOrWhiteSpace(descriptorId) || descriptorId.Any(char.IsWhiteSpace))
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.InvalidDescriptorId, location));
            if (capabilityVersion < 0)
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.NegativeCapabilityVersion, location));
            if (!IsSupportedDto(input))
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.UnsupportedInputType, location));
            if (!IsSupportedDto(output))
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.UnsupportedOutputType, location));
            if (!IsKnownSelectionPolicy(selectionPolicy)
                || !IsKnownSideEffectKind(sideEffectKind)
                || !IsKnownRiskFloor(riskFloor)
                || !IsKnownApprovalMode(approvalMode)
                || !IsKnownAuditMode(auditMode))
            {
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.InvalidEnum, location));
            }
            if (!AreRolesValid(roles))
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.InvalidAllowedRole, location));
            if (costUnits <= 0 || maxCallsPerExecution < 0)
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.InvalidBudget, location));
            if (IsUnsafeGovernance(sideEffectKind, riskFloor, approvalMode, auditMode))
                diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.UnsafeGovernance, location));

            // AgentToolSpec intentionally carries only Capability identity/version. There is no
            // stable compile-time CapabilityKind authoring contract to correlate here, so ATP015
            // must not be guessed from ids, names, or transport conventions. Startup validation
            // owns that check until a future same-compilation semantic kind source is introduced.

            specs.Add(new AgentToolSpecModel
            {
                SpecName = nested.Name,
                CapabilityId = capabilityId,
                CapabilityVersion = capabilityVersion,
                ExpectedCapabilityContractHash = GetString(attribute, "ExpectedCapabilityContractHash"),
                DescriptorId = descriptorId,
                DescriptorVersion = descriptorVersion,
                ToolName = toolName,
                Title = title,
                Description = description,
                InputType = input?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                OutputType = output?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                SelectionPolicy = selectionPolicy,
                SideEffectKind = sideEffectKind,
                RiskFloor = riskFloor,
                ApprovalMode = approvalMode,
                BudgetCategory = budgetCategory,
                CostUnits = costUnits,
                MaxCallsPerExecution = maxCallsPerExecution,
                AuditMode = auditMode,
                AllowedAgentRoles = roles
            });
        }

        foreach (var group in specs.GroupBy(static spec => spec.ToolName, StringComparer.Ordinal).Where(static group => group.Count() > 1))
            diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.DuplicateToolName, syntax.Identifier.GetLocation()));
        foreach (var group in specs.GroupBy(static spec => (spec.DescriptorId, spec.DescriptorVersion)).Where(static group => group.Count() > 1))
            diagnostics.Add(Diagnostic.Create(AgentToolDiagnostics.InvalidDescriptorId, syntax.Identifier.GetLocation()));

        return new AgentToolContainerModel
        {
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString(),
            Name = symbol.Name,
            Specs = specs.ToImmutable(),
            Diagnostics = diagnostics.ToImmutable()
        };
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol container)
    {
        foreach (var nested in container.GetTypeMembers())
        {
            yield return nested;
            foreach (var descendant in EnumerateNestedTypes(nested))
                yield return descendant;
        }
    }

    private static bool IsSupportedDto(ITypeSymbol? type)
    {
        if (type is null)
            return true;
        if (type is not INamedTypeSymbol named
            || named.IsUnboundGenericType
            || ContainsTypeParameter(named))
        {
            return false;
        }

        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return false;
        if (named.SpecialType != SpecialType.None
            || named.TypeKind is TypeKind.Interface or TypeKind.Dynamic or TypeKind.Enum or TypeKind.Delegate
            || named.IsAbstract
            || IsCollectionOrDictionary(named))
        {
            return false;
        }

        var containingNamespace = named.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (containingNamespace == "System"
            || containingNamespace.StartsWith("System.", StringComparison.Ordinal))
            return false;

        return named.TypeKind != TypeKind.Struct
            || named.GetMembers().OfType<IPropertySymbol>().Any(static property => !property.IsStatic);
    }

    private static bool ContainsTypeParameter(INamedTypeSymbol type)
        => type.TypeArguments.Any(static argument => argument.TypeKind == TypeKind.TypeParameter
            || argument is INamedTypeSymbol namedArgument && ContainsTypeParameter(namedArgument));

    private static bool IsCollectionOrDictionary(INamedTypeSymbol type)
    {
        var candidates = type.AllInterfaces.Concat(new[] { type });
        return candidates.Any(static candidate =>
        {
            var definition = candidate.OriginalDefinition.ToDisplayString();
            return definition is "System.Collections.IEnumerable"
                or "System.Collections.IDictionary"
                or "System.Collections.Generic.IEnumerable<T>"
                or "System.Collections.Generic.ICollection<T>"
                or "System.Collections.Generic.IReadOnlyCollection<T>"
                or "System.Collections.Generic.IDictionary<TKey, TValue>"
                or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>";
        });
    }

    private static bool AreRolesValid(ImmutableArray<string> roles)
        => roles.Length > 0
            && roles.All(static role => !string.IsNullOrWhiteSpace(role) && role != "*")
            && roles.Distinct(StringComparer.Ordinal).Count() == roles.Length;

    private static bool IsUnsafeGovernance(int sideEffectKind, int riskFloor, int approvalMode, int auditMode)
    {
        var requiresStrongGovernance = sideEffectKind is 3 or 4 || riskFloor is 3 or 4;
        if (requiresStrongGovernance && (approvalMode == 3 || auditMode != 1))
            return true;

        return auditMode == 2 && sideEffectKind != 1;
    }

    private static bool IsKnownSelectionPolicy(int value) => value is 1 or 2;
    private static bool IsKnownSideEffectKind(int value) => value >= 0 && value <= 4;
    private static bool IsKnownRiskFloor(int value) => value >= 0 && value <= 4;
    private static bool IsKnownApprovalMode(int value) => value is 1 or 2 or 3;
    private static bool IsKnownAuditMode(int value) => value is 1 or 2;

    private static string HintName(AgentToolContainerModel container, string suffix)
    {
        var identity = string.IsNullOrEmpty(container.Namespace)
            ? container.Name
            : container.Namespace + "." + container.Name;
        var sanitized = Regex.Replace(identity, "[^A-Za-z0-9_.-]", "_");
        return sanitized + "_AgentTool" + suffix + ".g.cs";
    }

    private static string? GetString(AttributeData attribute, string name)
        => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;

    private static int GetInt(AttributeData attribute, string name, int fallback)
    {
        var value = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value;
        return value is int number ? number : fallback;
    }

    private static long GetLong(AttributeData attribute, string name, long fallback)
    {
        var value = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value;
        return value is long number ? number : fallback;
    }

    private static ITypeSymbol? GetType(AttributeData attribute, string name)
        => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as ITypeSymbol;

    private static IEnumerable<string> GetStringArray(AttributeData attribute, string name)
    {
        var value = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value;
        if (value.Kind != TypedConstantKind.Array || value.IsNull)
            return Enumerable.Empty<string>();

        return value.Values.Select(static item => item.Value as string ?? string.Empty);
    }
}
