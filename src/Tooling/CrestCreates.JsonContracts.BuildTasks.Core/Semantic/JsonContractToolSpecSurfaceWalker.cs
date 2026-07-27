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
        JsonContractRootSourceKind inputSourceKind,
        JsonContractRootSourceKind outputSourceKind,
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

            AddNamedRoot(attribute, "InputType", inputSourceKind, container, spec, contextMetadataName, roots);
            AddNamedRoot(attribute, "OutputType", outputSourceKind, container, spec, contextMetadataName, roots);
        }

        var result = roots.Values.ToList();
        result.Sort((left, right) => string.Compare(left.FullMetadataName, right.FullMetadataName, StringComparison.Ordinal));
        return result;
    }

    private void AddNamedRoot(
        AttributeData attribute,
        string role,
        JsonContractRootSourceKind sourceKind,
        INamedTypeSymbol container,
        INamedTypeSymbol spec,
        string contextMetadataName,
        Dictionary<ITypeSymbol, JsonContractRootModel> roots)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == role);
        if (argument.Key is null || argument.Value.Value is null)
            return;

        if (argument.Value.Value is not ITypeSymbol type)
            return;

        var origin = new JsonContractRootOrigin
        {
            SourceKind = sourceKind,
            DeclaringSurface = container.ToDisplayString(s_format),
            DeclarationName = spec.Name,
            RoleName = role,
            Location = spec.Locations.FirstOrDefault(),
        };
        var diagnostic = JsonContractRootValidator.Validate(type, origin, contextMetadataName);
        if (diagnostic is not null)
        {
            _diagnostics.Add(diagnostic);
            return;
        }

        var normalized = JsonContractRootNormalizer.Normalize(type);

        if (!roots.TryGetValue(normalized, out var root))
        {
            root = new JsonContractRootModel
            {
                RootType = normalized,
                FullMetadataName = normalized.ToDisplayString(s_format),
                Provenance = new JsonContractRootProvenance
                {
                    Origins = [origin],
                },
            };
            roots.Add(normalized, root);
            return;
        }

        if (!root.Provenance.Origins.Any(existing => existing.Identity == origin.Identity))
        {
            root.Provenance.Origins.Add(origin);
            root.Provenance.Origins.Sort((left, right) =>
                string.Compare(left.Identity, right.Identity, StringComparison.Ordinal));
        }
    }
}
