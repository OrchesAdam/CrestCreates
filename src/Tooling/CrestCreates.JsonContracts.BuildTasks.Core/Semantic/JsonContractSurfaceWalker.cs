using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.JsonContracts.BuildTasks.Semantic;

public sealed class JsonContractSurfaceWalker
{
    private static readonly SymbolDisplayFormat s_canonicalFormat =
        new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                | SymbolDisplayMiscellaneousOptions.UseErrorTypeSymbolName);

    private readonly List<JsonContractDiagnostic> _diagnostics = [];

    public List<JsonContractRootModel> WalkSurface(
        INamedTypeSymbol surfaceType,
        INamedTypeSymbol contextType,
        INamedTypeSymbol cancellationTokenSymbol,
        INamedTypeSymbol taskSymbol,
        INamedTypeSymbol valueTaskSymbol,
        INamedTypeSymbol markerSymbol,
        INamedTypeSymbol serializableSymbol,
        HashSet<ITypeSymbol> excludedParameterTypes,
        string contextMetadataName)
    {
        _diagnostics.Clear();

        var visitedInterfaces = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var allMethods = new List<(INamedTypeSymbol DeclaringInterface, IMethodSymbol Method)>();

        CollectMethods(surfaceType, surfaceType, visitedInterfaces, allMethods);

        var dedupedMethods = DeduplicateMethods(allMethods);

        var roots = new Dictionary<ITypeSymbol, JsonContractRootModel>(SymbolEqualityComparer.Default);

        foreach (var (declaringInterface, method) in dedupedMethods)
        {
            if (method.MethodKind != MethodKind.Ordinary)
                continue;

            if (method.IsStatic)
                continue;

            if (method.IsGenericMethod)
            {
                _diagnostics.Add(new JsonContractDiagnostic
                {
                    Id = JsonContractDiagnosticIds.GenericMethod,
                    Message = $"Method '{method.ToDisplayString()}' on surface '{declaringInterface.ToDisplayString()}' is generic. Generic methods are not supported.",
                    Severity = JsonContractDiagnosticSeverity.Error,
                    ContextMetadataName = contextMetadataName,
                    MethodSignature = method.ToDisplayString(s_canonicalFormat),
                });
                continue;
            }

            if (method.ReturnsByRef || method.ReturnsByRefReadonly)
            {
                _diagnostics.Add(new JsonContractDiagnostic
                {
                    Id = JsonContractDiagnosticIds.InvalidRoot,
                    Message = $"Method '{method.ToDisplayString()}' returns by reference. By-ref returns are not supported.",
                    Severity = JsonContractDiagnosticSeverity.Error,
                    ContextMetadataName = contextMetadataName,
                    MethodSignature = method.ToDisplayString(s_canonicalFormat),
                });
                continue;
            }

            var returnType = method.ReturnType;
            if (returnType is null)
                continue;

            var unwrappedReturn = UnwrapTask(returnType, taskSymbol, valueTaskSymbol);
            if (unwrappedReturn is not null)
            {
                AddRoot(
                    unwrappedReturn,
                    new JsonContractRootOrigin
                    {
                        SourceKind = JsonContractRootSourceKind.InterfaceReturn,
                        DeclaringSurface = declaringInterface.ToDisplayString(s_canonicalFormat),
                        MemberSignature = method.ToDisplayString(s_canonicalFormat),
                        RoleName = "ReturnType",
                        Location = method.Locations.FirstOrDefault(),
                    },
                    roots,
                    contextMetadataName);
            }

            foreach (var param in method.Parameters)
            {
                if (param.RefKind != RefKind.None)
                {
                    _diagnostics.Add(new JsonContractDiagnostic
                    {
                        Id = JsonContractDiagnosticIds.ByRefPointerOrRefLikeParameter,
                        Message = $"Parameter '{param.Name}' of method '{method.ToDisplayString()}' has ref/out/in modifier. By-ref parameters are not supported.",
                        Severity = JsonContractDiagnosticSeverity.Error,
                        ContextMetadataName = contextMetadataName,
                        MethodSignature = method.ToDisplayString(s_canonicalFormat),
                        ParameterName = param.Name,
                    });
                    continue;
                }

                var paramType = param.Type;
                if (paramType is null)
                    continue;

                if (IsExactSymbolMatch(paramType, cancellationTokenSymbol))
                    continue;

                if (excludedParameterTypes.Any(e => IsExactSymbolMatch(paramType, e)))
                    continue;

                AddRoot(
                    paramType,
                    new JsonContractRootOrigin
                    {
                        SourceKind = JsonContractRootSourceKind.InterfaceParameter,
                        DeclaringSurface = declaringInterface.ToDisplayString(s_canonicalFormat),
                        MemberSignature = method.ToDisplayString(s_canonicalFormat),
                        RoleName = param.Name,
                        Location = param.Locations.FirstOrDefault(),
                    },
                    roots,
                    contextMetadataName);
            }
        }

        return roots.Values.ToList();
    }

    public IReadOnlyList<JsonContractDiagnostic> Diagnostics => _diagnostics;

    private void CollectMethods(
        INamedTypeSymbol currentInterface,
        INamedTypeSymbol rootSurface,
        HashSet<INamedTypeSymbol> visitedInterfaces,
        List<(INamedTypeSymbol DeclaringInterface, IMethodSymbol Method)> allMethods)
    {
        if (!visitedInterfaces.Add(currentInterface))
            return;

        foreach (var member in currentInterface.GetMembers())
        {
            if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary && !method.IsStatic)
            {
                allMethods.Add((rootSurface, method));
            }
        }

        foreach (var baseInterface in currentInterface.AllInterfaces)
        {
            if (!visitedInterfaces.Contains(baseInterface))
            {
                foreach (var member in baseInterface.GetMembers())
                {
                    if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary && !method.IsStatic)
                    {
                        allMethods.Add((rootSurface, method));
                    }
                }
                visitedInterfaces.Add(baseInterface);
            }
        }
    }

    private static List<(INamedTypeSymbol DeclaringInterface, IMethodSymbol Method)> DeduplicateMethods(
        List<(INamedTypeSymbol DeclaringInterface, IMethodSymbol Method)> allMethods)
    {
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var result = new List<(INamedTypeSymbol, IMethodSymbol)>();

        foreach (var entry in allMethods)
        {
            if (seen.Add(entry.Method))
                result.Add(entry);
        }

        return result;
    }

    private static ITypeSymbol? UnwrapTask(ITypeSymbol returnType, INamedTypeSymbol task1Symbol, INamedTypeSymbol valueTask1Symbol)
    {
        if (returnType.SpecialType == SpecialType.System_Void)
            return null;

        if (returnType is not INamedTypeSymbol namedReturn)
            return returnType;

        if (namedReturn.Arity == 0)
        {
            var metadataName = namedReturn.OriginalDefinition?.ToDisplayString(s_canonicalFormat);
            if (metadataName == "global::System.Threading.Tasks.Task"
                || metadataName == "global::System.Threading.Tasks.ValueTask")
                return null;
        }

        if (namedReturn.Arity == 1)
        {
            if (SymbolEqualityComparer.Default.Equals(namedReturn.OriginalDefinition, task1Symbol))
                return namedReturn.TypeArguments[0];

            if (SymbolEqualityComparer.Default.Equals(namedReturn.OriginalDefinition, valueTask1Symbol))
                return namedReturn.TypeArguments[0];
        }

        return returnType;
    }

    private void AddRoot(
        ITypeSymbol type,
        JsonContractRootOrigin origin,
        Dictionary<ITypeSymbol, JsonContractRootModel> roots,
        string contextMetadataName)
    {
        var diagnostic = JsonContractRootValidator.Validate(type, origin, contextMetadataName);
        if (diagnostic is not null)
        {
            _diagnostics.Add(diagnostic);
            return;
        }

        var normalizedType = JsonContractRootNormalizer.Normalize(type);

        if (!roots.TryGetValue(normalizedType, out var existing))
        {
            roots[normalizedType] = new JsonContractRootModel
            {
                RootType = normalizedType,
                FullMetadataName = normalizedType.ToDisplayString(s_canonicalFormat),
                Provenance = new JsonContractRootProvenance
                {
                    Origins = [origin],
                },
            };
        }
        else
        {
            AddOrigin(existing.Provenance, origin);
        }
    }

    private static void AddOrigin(JsonContractRootProvenance provenance, JsonContractRootOrigin origin)
    {
        if (provenance.Origins.Any(existing => existing.Identity == origin.Identity))
            return;

        provenance.Origins.Add(origin);
        provenance.Origins.Sort((left, right) => string.Compare(left.Identity, right.Identity, StringComparison.Ordinal));
    }

    private static bool IsExactSymbolMatch(ITypeSymbol type, ITypeSymbol target) =>
        SymbolEqualityComparer.Default.Equals(type, target);
}
