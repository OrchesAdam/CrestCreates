using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using Microsoft.CodeAnalysis;

namespace CrestCreates.JsonContracts.BuildTasks.Semantic;

public sealed class JsonContractToolSpecSurfaceWalker
{
    private static readonly SymbolDisplayFormat s_format =
        new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                | SymbolDisplayMiscellaneousOptions.UseErrorTypeSymbolName);

    private readonly List<JsonContractDiagnostic> _diagnostics = [];

    public IReadOnlyList<JsonContractDiagnostic> Diagnostics => _diagnostics;

    public List<JsonContractRootModel> WalkSurface(
        INamedTypeSymbol container,
        INamedTypeSymbol specAttribute,
        string adapterName,
        string contextMetadataName)
    {
        _diagnostics.Clear();
        var roots = new Dictionary<ITypeSymbol, JsonContractRootModel>(SymbolEqualityComparer.Default);

        foreach (var spec in container.GetTypeMembers())
        {
            if (spec.TypeKind != TypeKind.Class || spec.IsStatic || spec.Arity != 0)
                continue;

            var attribute = spec.GetAttributes().FirstOrDefault(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, specAttribute));
            if (attribute is null)
                continue;

            AddNamedRoot(attribute, "InputType", container, spec, adapterName, contextMetadataName, roots);
            AddNamedRoot(attribute, "OutputType", container, spec, adapterName, contextMetadataName, roots);
        }

        var result = roots.Values.ToList();
        result.Sort((left, right) => string.Compare(left.FullMetadataName, right.FullMetadataName, StringComparison.Ordinal));
        return result;
    }

    private void AddNamedRoot(
        AttributeData attribute,
        string role,
        INamedTypeSymbol container,
        INamedTypeSymbol spec,
        string adapterName,
        string contextMetadataName,
        Dictionary<ITypeSymbol, JsonContractRootModel> roots)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == role);
        if (argument.Key is null || argument.Value.Value is null)
            return;

        if (argument.Value.Value is not ITypeSymbol type)
            return;

        if (!ValidateRoot(type, contextMetadataName, spec, role))
            return;

        var normalized = JsonContractRootNormalizer.Normalize(type);
        var declaration = $"{adapterName}:{container.ToDisplayString(s_format)}::{spec.Name}.{role}";

        if (!roots.TryGetValue(normalized, out var root))
        {
            root = new JsonContractRootModel
            {
                RootType = normalized,
                FullMetadataName = normalized.ToDisplayString(s_format),
                Provenance = new JsonContractRootProvenance
                {
                    DeclaringSurface = container.ToDisplayString(s_format),
                    Declarations = [declaration],
                    IsReturnRoot = role == "OutputType",
                },
            };
            roots.Add(normalized, root);
            return;
        }

        if (!root.Provenance.Declarations.Contains(declaration))
        {
            root.Provenance.Declarations.Add(declaration);
            root.Provenance.Declarations.Sort(StringComparer.Ordinal);
        }

        if (role == "OutputType")
            root.Provenance.IsReturnRoot = true;
    }

    private bool ValidateRoot(
        ITypeSymbol type,
        string contextMetadataName,
        INamedTypeSymbol spec,
        string role)
    {
        if (type is IErrorTypeSymbol)
        {
            Report(JsonContractDiagnosticIds.UnresolvedPreCoreCompileRoot,
                $"Tool spec root '{type.ToDisplayString()}' is unresolved. Move the contract to a referenced assembly or an earlier MSBuild compile source.",
                type, contextMetadataName, spec, role);
            return false;
        }

        if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
        {
            Report(JsonContractDiagnosticIds.ByRefPointerOrRefLikeParameter,
                $"Tool spec root '{type.ToDisplayString()}' is a pointer or function pointer and is not supported.",
                type, contextMetadataName, spec, role);
            return false;
        }

        if (type is not INamedTypeSymbol namedType)
            return true;

        if (namedType.IsUnboundGenericType
            || namedType.Arity > 0 && namedType.TypeArguments.Any(argument => argument.TypeKind == TypeKind.TypeParameter))
        {
            Report(JsonContractDiagnosticIds.InvalidRoot,
                $"Tool spec root '{type.ToDisplayString()}' is an open generic. Only closed generic roots are supported.",
                type, contextMetadataName, spec, role);
            return false;
        }

        if (namedType.IsRefLikeType)
        {
            Report(JsonContractDiagnosticIds.ByRefPointerOrRefLikeParameter,
                $"Tool spec root '{type.ToDisplayString()}' is ref-like and is not supported.",
                type, contextMetadataName, spec, role);
            return false;
        }

        if (namedType.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
        {
            Report(JsonContractDiagnosticIds.InaccessibleRoot,
                $"Tool spec root '{type.ToDisplayString()}' is not accessible.",
                type, contextMetadataName, spec, role);
            return false;
        }

        return true;
    }

    private void Report(
        string id,
        string message,
        ITypeSymbol type,
        string contextMetadataName,
        INamedTypeSymbol spec,
        string role)
        => _diagnostics.Add(new JsonContractDiagnostic
        {
            Id = id,
            Message = message,
            Severity = JsonContractDiagnosticSeverity.Error,
            ContextMetadataName = contextMetadataName,
            SurfaceMetadataName = spec.ToDisplayString(s_format),
            OffendingType = type.ToDisplayString(s_format),
            ParameterName = role,
        }.WithLocation(spec.Locations.FirstOrDefault()));

}
