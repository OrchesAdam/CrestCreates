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
        var agentToolSpecsSymbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.AgentToolSpecsAttribute);
        var agentToolSpecSymbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.AgentToolSpecAttribute);
        var mcpToolSpecsSymbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.McpToolSpecsAttribute);
        var mcpToolSpecSymbol = compilation.GetTypeByMetadataName(JsonContractSymbolNames.McpToolSpecAttribute);

        if (markerSymbol is null || contextSymbol is null || serializableSymbol is null || explicitRootSymbol is null)
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = JsonContractDiagnosticIds.RequiredSymbolUnresolved,
                Message = BuildRequiredSymbolMessage(markerSymbol, contextSymbol, serializableSymbol, explicitRootSymbol),
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
            CollectContexts(type, markerSymbol, contextSymbol, serializableSymbol, explicitRootSymbol,
                agentToolSpecsSymbol, agentToolSpecSymbol, mcpToolSpecsSymbol, mcpToolSpecSymbol,
                compilation, contexts);
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
        INamedTypeSymbol? agentToolSpecsSymbol,
        INamedTypeSymbol? agentToolSpecSymbol,
        INamedTypeSymbol? mcpToolSpecsSymbol,
        INamedTypeSymbol? mcpToolSpecSymbol,
        CSharpCompilation compilation,
        List<JsonContractContextModel> contexts)
    {
        var surfaceAttributes = type.GetAttributes()
            .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, markerSymbol))
            .ToList();

        if (surfaceAttributes.Count == 0)
            return;

        if (!type.DerivesFrom(contextBaseSymbol))
        {
            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = JsonContractDiagnosticIds.InvalidContext,
                Message = $"Marked type '{type.ToDisplayString()}' must derive from JsonSerializerContext.",
                Severity = JsonContractDiagnosticSeverity.Error,
                ContextMetadataName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            }.WithLocation(type.Locations.FirstOrDefault()));
            return;
        }

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
        var bindingRoots = new List<JsonContractRootModel>();

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

            if (surfaceType.TypeKind == TypeKind.Interface && !surfaceType.IsUnboundGenericType)
            {
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
                continue;
            }

            var validToolSpecContainer = surfaceType.IsStatic
                && surfaceType.ContainingType is null
                && surfaceType.Arity == 0;
            var agentAdapter = validToolSpecContainer
                && agentToolSpecsSymbol is not null && agentToolSpecSymbol is not null
                && surfaceType.GetAttributes().Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, agentToolSpecsSymbol));
            var mcpAdapter = validToolSpecContainer
                && mcpToolSpecsSymbol is not null && mcpToolSpecSymbol is not null
                && surfaceType.GetAttributes().Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, mcpToolSpecsSymbol));

            if (agentAdapter ^ mcpAdapter)
            {
                var walker = new JsonContractToolSpecSurfaceWalker();
                var walkedRoots = walker.WalkSurface(
                    surfaceType,
                    agentAdapter ? agentToolSpecSymbol! : mcpToolSpecSymbol!,
                    agentAdapter ? JsonContractRootSourceKind.AgentToolInput : JsonContractRootSourceKind.McpToolInput,
                    agentAdapter ? JsonContractRootSourceKind.AgentToolOutput : JsonContractRootSourceKind.McpToolOutput,
                    metadataName);
                _diagnostics.AddRange(walker.Diagnostics);
                surfaceRoots.AddRange(walkedRoots);
                bindingRoots.AddRange(walkedRoots.Select(CloneRoot));
                continue;
            }

            _diagnostics.Add(new JsonContractDiagnostic
            {
                Id = JsonContractDiagnosticIds.InvalidSurface,
                Message = $"Surface type '{surfaceType.ToDisplayString()}' must be a closed interface or an exact Agent/MCP Tool-Spec container.",
                Severity = JsonContractDiagnosticSeverity.Error,
                ContextMetadataName = metadataName,
                SurfaceMetadataName = surfaceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            }.WithLocation(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()));
        }

        surfaceRoots = DeduplicateRoots(surfaceRoots);
        bindingRoots = DeduplicateRoots(bindingRoots);

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
            BindingRoots = bindingRoots,
            ExplicitRoots = explicitRootList,
            AllDirectRoots = allDirectRoots,
            ManifestAccessibility = manifestAccessibility,
            ManifestClassName = manifestClassName,
        });
    }

    private static string BuildRequiredSymbolMessage(
        INamedTypeSymbol? marker,
        INamedTypeSymbol? context,
        INamedTypeSymbol? serializable,
        INamedTypeSymbol? explicitRoot)
    {
        var missing = new List<string>();
        if (marker is null) missing.Add(JsonContractSymbolNames.MarkerAttribute);
        if (context is null) missing.Add(JsonContractSymbolNames.JsonSerializerContext);
        if (serializable is null) missing.Add(JsonContractSymbolNames.JsonSerializableAttribute);
        if (explicitRoot is null) missing.Add(JsonContractSymbolNames.JsonContractExplicitRootAttribute);
        return $"Required symbol(s) unresolved: {string.Join(", ", missing)}. Ensure System.Text.Json and CrestCreates.Core.Abstractions are referenced.";
    }

    private static HashSet<ITypeSymbol> GetExcludedParameterTypes(AttributeData attr, CSharpCompilation compilation)
    {
        var result = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg.Key != "ExcludedParameterTypes")
                continue;

            if (namedArg.Value.Kind != TypedConstantKind.Array)
                continue;

            foreach (var elem in namedArg.Value.Values)
            {
                if (elem.Value is ITypeSymbol excludedType)
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
                foreach (var origin in root.Provenance.Origins)
                {
                    if (!existing.Provenance.Origins.Any(candidate => candidate.Identity == origin.Identity))
                        existing.Provenance.Origins.Add(origin);
                }
                existing.Provenance.Origins.Sort((left, right) =>
                    string.Compare(left.Identity, right.Identity, StringComparison.Ordinal));
            }
        }

        var result = deduped.Values.ToList();
        result.Sort((a, b) => string.Compare(a.FullMetadataName, b.FullMetadataName, StringComparison.Ordinal));
        return result;
    }

    private static JsonContractRootModel CloneRoot(JsonContractRootModel root)
        => new()
        {
            RootType = root.RootType,
            FullMetadataName = root.FullMetadataName,
            IsExplicitExtra = root.IsExplicitExtra,
            Provenance = new JsonContractRootProvenance
            {
                Origins = [.. root.Provenance.Origins],
            },
        };

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

            var origin = new JsonContractRootOrigin
            {
                SourceKind = JsonContractRootSourceKind.Explicit,
                DeclaringSurface = contextMetadataName,
                RoleName = "ExplicitRoot",
                Location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
            };
            var diagnostic = JsonContractRootValidator.Validate(rootType, origin, contextMetadataName);
            if (diagnostic is not null)
            {
                diagnostics.Add(diagnostic);
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
                    Origins = [origin],
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
                foreach (var origin in root.Provenance.Origins)
                {
                    if (!existing.Provenance.Origins.Any(candidate => candidate.Identity == origin.Identity))
                        existing.Provenance.Origins.Add(origin);
                }
                existing.Provenance.Origins.Sort((left, right) =>
                    string.Compare(left.Identity, right.Identity, StringComparison.Ordinal));
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
