using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.JsonContracts.BuildTasks.Semantic;

public sealed class JsonContractSurfaceModelBuilder
{
    private readonly List<JsonContractDiagnostic> _diagnostics = [];

    public JsonContractGenerationModel Build(CSharpCompilation compilation)
    {
        _diagnostics.Clear();

        var markerSymbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.MarkerAttribute);
        var contextSymbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.JsonSerializerContext);
        var serializableSymbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.JsonSerializableAttribute);
        var explicitRootSymbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.JsonContractExplicitRootAttribute);

        if (markerSymbol is null || contextSymbol is null || serializableSymbol is null)
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = JsonContractDiagnosticIds.RequiredSymbolUnresolved,
                Message = BuildRequiredSymbolMessage(markerSymbol, contextSymbol, serializableSymbol),
                Severity = JsonContractDiagnosticSeverity.Error,
            });

            return new JsonContractGenerationModel
            {
                Contexts = [],
                Diagnostics = [.. _diagnostics],
            };
        }

        var contexts = new List<JsonContractContextModel>();

        foreach (var type in compilation.SourceModule.GlobalNamespace.GetAllTypes())
        {
            CollectContexts(type, markerSymbol, contextSymbol, serializableSymbol, explicitRootSymbol, compilation, contexts);
        }

        contexts.Sort((a, b) => string.Compare(a.FullMetadataName, b.FullMetadataName, StringComparison.Ordinal));

        return new JsonContractGenerationModel
        {
            Contexts = contexts,
            Diagnostics = [.. _diagnostics],
        };
    }

    private void CollectContexts(
        INamedTypeSymbol type,
        INamedTypeSymbol markerSymbol,
        INamedTypeSymbol contextBaseSymbol,
        INamedTypeSymbol serializableSymbol,
        INamedTypeSymbol explicitRootSymbol,
        CSharpCompilation compilation,
        List<JsonContractContextModel> contexts)
    {
        if (!type.DerivesFrom(contextBaseSymbol))
            return;

        var surfaceAttributes = type.GetAttributes()
            .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, markerSymbol))
            .ToList();

        if (surfaceAttributes.Count == 0)
            return;

        if (!type.IsPartial())
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = JsonContractDiagnosticIds.InvalidContext,
                Message = $"Context type '{type.ToDisplayString()}' must be declared as partial.",
                Severity = JsonContractDiagnosticSeverity.Error,
                ContextMetadataName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            }.WithLocation(type.Locations.FirstOrDefault()));
            return;
        }

        if (type.ContainingType is not null)
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = JsonContractDiagnosticIds.InvalidContext,
                Message = $"Context type '{type.ToDisplayString()}' must be a top-level type, not nested.",
                Severity = JsonContractDiagnosticSeverity.Error,
                ContextMetadataName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            }.WithLocation(type.Locations.FirstOrDefault()));
            return;
        }

        if (type.IsUnboundGenericType || type.Arity > 0)
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = JsonContractDiagnosticIds.InvalidContext,
                Message = $"Context type '{type.ToDisplayString()}' must not be generic.",
                Severity = JsonContractDiagnosticSeverity.Error,
                ContextMetadataName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            }.WithLocation(type.Locations.FirstOrDefault()));
            return;
        }

        var metadataName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var containingNamespace = type.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var simpleName = type.Name;

        var surfaceRoots = new List<JsonContractRootModel>();

        var cancellationTokenSymbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.CancellationToken);
        var taskSymbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.Task);
        var task1Symbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.Task1);
        var valueTaskSymbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.ValueTask);
        var valueTask1Symbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.ValueTask1);

        foreach (var attr in surfaceAttributes)
        {
            var surfaceTypeArg = attr.ConstructorArguments.FirstOrDefault();
            if (surfaceTypeArg.Value is not INamedTypeSymbol surfaceType)
            {
                _diagnostics.Add(new JsonContractDiagnostic
                {
                    Id = JsonContractDiagnosticIds.InvalidSurface,
                    Message = "JsonContractSurface attribute must specify a valid type.",
                    Severity = JsonContractDiagnosticSeverity.Error,
                    ContextMetadataName = metadataName,
                }.WithLocation(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()));
                continue;
            }

            if (surfaceType.TypeKind != TypeKind.Interface || surfaceType.IsUnboundGenericType)
            {
                _diagnostics.Add(new JsonContractDiagnostic
                {
                    Id = JsonContractDiagnosticIds.InvalidSurface,
                    Message = $"Surface type '{surfaceType.ToDisplayString()}' must be a closed interface.",
                    Severity = JsonContractDiagnosticSeverity.Error,
                    ContextMetadataName = metadataName,
                    SurfaceMetadataName = surfaceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                }.WithLocation(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()));
                continue;
            }

            if (cancellationTokenSymbol is null || task1Symbol is null || valueTask1Symbol is null)
                continue;

            var excludedParamTypes = GetExcludedParameterTypes(attr, compilation);

            var walker = new JsonContractSurfaceWalker();
            var walkedRoots = walker.WalkSurface(
                surfaceType, type,
                cancellationTokenSymbol, task1Symbol, valueTask1Symbol,
                markerSymbol, serializableSymbol,
                excludedParamTypes,
                metadataName);

            _diagnostics.AddRange(walker.Diagnostics);
            surfaceRoots.AddRange(walkedRoots);
        }

        surfaceRoots = DeduplicateRoots(surfaceRoots);

        var explicitRoots = CollectExplicitRoots(type, explicitRootSymbol, metadataName);
        _diagnostics.AddRange(explicitRoots.Diagnostics);
        var explicitRootList = explicitRoots.Roots;

        var allDirectRoots = MergeSurfaceAndExplicitRoots(surfaceRoots, explicitRootList);

        var declaredAccessibility = type.DeclaredAccessibility == Accessibility.Public ? "Public" : "Internal";
        var manifestAccessibility = JsonContractManifestAccessibility.Internal;
        var manifestClassName = $"{simpleName}RootManifest";

        contexts.Add(new JsonContractContextModel
        {
            ContextSymbol = type,
            FullMetadataName = metadataName,
            ContainingNamespace = containingNamespace,
            ContextSimpleName = simpleName,
            DeclaredAccessibility = declaredAccessibility,
            SurfaceRoots = surfaceRoots,
            ExplicitRoots = explicitRootList,
            AllDirectRoots = allDirectRoots,
            ManifestAccessibility = manifestAccessibility,
            ManifestClassName = manifestClassName,
        });
    }

    private static string BuildRequiredSymbolMessage(
        INamedTypeSymbol? marker,
        INamedTypeSymbol? context,
        INamedTypeSymbol? serializable)
    {
        var missing = new List<string>();
        if (marker is null) missing.Add(JsonContractSymbolNames.MarkerAttribute);
        if (context is null) missing.Add(JsonContractSymbolNames.JsonSerializerContext);
        if (serializable is null) missing.Add(JsonContractSymbolNames.JsonSerializableAttribute);
        return $"Required symbol(s) unresolved: {string.Join(", ", missing)}. Ensure System.Text.Json and CrestCreates.Core.Abstractions are referenced.";
    }

    private static HashSet<INamedTypeSymbol> GetExcludedParameterTypes(AttributeData attr, CSharpCompilation compilation)
    {
        var result = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg.Key != "ExcludedParameterTypes")
                continue;

            if (namedArg.Value.Kind != TypedConstantKind.Array)
                continue;

            foreach (var elem in namedArg.Value.Values)
            {
                if (elem.Value is INamedTypeSymbol excludedType)
                    result.Add(excludedType);
            }
        }

        return result;
    }

    private static List<JsonContractRootModel> DeduplicateRoots(List<JsonContractRootModel> roots)
    {
        var deduped = new Dictionary<ITypeSymbol, JsonContractRootModel>(SymbolEqualityComparer.Default);

        foreach (var root in roots)
        {
            if (!deduped.TryGetValue(root.RootType, out var existing))
            {
                deduped[root.RootType] = root;
            }
            else
            {
                foreach (var sig in root.Provenance.MethodSignatures)
                {
                    if (!existing.Provenance.MethodSignatures.Contains(sig))
                    {
                        existing.Provenance.MethodSignatures.Add(sig);
                    }
                }
                existing.Provenance.MethodSignatures.Sort(StringComparer.Ordinal);
                if (root.Provenance.IsReturnRoot)
                    existing.Provenance.IsReturnRoot = true;
            }
        }

        var result = deduped.Values.ToList();
        result.Sort((a, b) => string.Compare(a.FullMetadataName, b.FullMetadataName, StringComparison.Ordinal));
        return result;
    }

    private (List<JsonContractRootModel> Roots, List<JsonContractDiagnostic> Diagnostics) CollectExplicitRoots(
        INamedTypeSymbol contextType,
        INamedTypeSymbol explicitRootSymbol,
        string contextMetadataName)
    {
        var diagnostics = new List<JsonContractDiagnostic>();
        var roots = new List<JsonContractRootModel>();

        foreach (var attr in contextType.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, explicitRootSymbol))
                continue;

            var typeArg = attr.ConstructorArguments.FirstOrDefault();
            if (typeArg.Value is not ITypeSymbol rootType)
                continue;

            if (rootType is IErrorTypeSymbol)
            {
                diagnostics.Add(new JsonContractDiagnostic
                {
                    Id = JsonContractDiagnosticIds.UnresolvedPreCoreCompileRoot,
                    Message = $"Explicit root type '{rootType.ToDisplayString()}' is unresolved. Move the contract to a referenced assembly, add an earlier MSBuild compile source, or retain an explicit visible root.",
                    Severity = JsonContractDiagnosticSeverity.Error,
                    ContextMetadataName = contextMetadataName,
                    OffendingType = rootType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                });
                continue;
            }

            var normalizedType = JsonContractRootNormalizer.Normalize(rootType);

            roots.Add(new JsonContractRootModel
            {
                RootType = normalizedType,
                FullMetadataName = normalizedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsExplicitExtra = true,
                Provenance = new JsonContractRootProvenance
                {
                    DeclaringSurface = contextMetadataName,
                    MethodSignatures = [],
                    IsReturnRoot = false,
                },
            });
        }

        return (roots, diagnostics);
    }

    private static List<JsonContractRootModel> MergeSurfaceAndExplicitRoots(
        List<JsonContractRootModel> surfaceRoots,
        List<JsonContractRootModel> explicitRoots)
    {
        var merged = new Dictionary<ITypeSymbol, JsonContractRootModel>(SymbolEqualityComparer.Default);

        foreach (var root in surfaceRoots)
        {
            merged[root.RootType] = root;
        }

        foreach (var root in explicitRoots)
        {
            if (!merged.TryGetValue(root.RootType, out var existing))
            {
                merged[root.RootType] = root;
            }
            else
            {
                existing.IsExplicitExtra = true;
            }
        }

        var result = merged.Values.ToList();
        result.Sort((a, b) => string.Compare(a.FullMetadataName, b.FullMetadataName, StringComparison.Ordinal));
        return result;
    }

    public static JsonContractManifestAccessibility ParseManifestAccessibility(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return JsonContractManifestAccessibility.Internal;

        var trimmed = value.Trim();
        if (trimmed.Equals("Internal", StringComparison.Ordinal))
            return JsonContractManifestAccessibility.Internal;
        if (trimmed.Equals("Public", StringComparison.Ordinal))
            return JsonContractManifestAccessibility.Public;

        throw new InvalidOperationException($"Invalid manifest accessibility '{value}'. Must be 'Internal' or 'Public'.");
    }
}
