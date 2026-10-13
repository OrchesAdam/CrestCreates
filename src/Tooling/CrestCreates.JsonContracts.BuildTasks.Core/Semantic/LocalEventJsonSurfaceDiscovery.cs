using CrestCreates.JsonContracts.BuildTasks.Diagnostics;
using CrestCreates.JsonContracts.BuildTasks.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.JsonContracts.BuildTasks.Semantic;

/// <summary>
/// Discovers [LocalEvent(Replayable = true)] declarations in the project sources and turns them
/// into source-generated JSON codec roots for the local event replay pipeline. This is the
/// pre-compile half of the LocalEvent codec contract: the roots plus a static codec contribution
/// are emitted into the existing generated file, consumed by the official STJ generator during
/// CoreCompile — never by a second source generator reading another generator's output.
/// </summary>
public sealed class LocalEventJsonSurfaceDiscovery
{
    private readonly List<JsonContractDiagnostic> _diagnostics = [];

    public IReadOnlyList<JsonContractDiagnostic> Diagnostics => _diagnostics;

    public List<JsonContractRootModel> Discover(CSharpCompilation compilation)
    {
        var roots = new List<JsonContractRootModel>();

        var attributeSymbol = compilation.GetTypeByMetadataName(JsonContractBuildConstants.LocalEventAttributeFullName);
        if (attributeSymbol is null)
        {
            return roots;
        }

        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var type in compilation.SourceModule.GlobalNamespace.GetAllTypes())
        {
            var attribute = type.GetAttributes()
                .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attributeSymbol));

            if (attribute is null)
            {
                continue;
            }

            var replayable = attribute.NamedArguments
                .Any(named => named.Key == "Replayable" && named.Value.Value is true);
            if (!replayable)
            {
                continue;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();

            if (!seen.Add(type))
            {
                continue;
            }

            if (type.IsAbstract || type.IsGenericType || type.TypeKind != TypeKind.Class)
            {
                _diagnostics.Add(new JsonContractDiagnostic
                {
                    Id = JsonContractDiagnosticIds.LocalEventInvalidPayload,
                    Message = $"Replayable local event '{type.ToDisplayString()}' must be a closed, non-abstract class; " +
                              "open generics, abstract classes and other shapes have no closed JSON payload contract.",
                    Severity = JsonContractDiagnosticSeverity.Error,
                    OffendingType = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                }.WithLocation(location));
                continue;
            }

            if (type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                _diagnostics.Add(new JsonContractDiagnostic
                {
                    Id = JsonContractDiagnosticIds.LocalEventInaccessiblePayload,
                    Message = $"Replayable local event '{type.ToDisplayString()}' must be public or internal so the " +
                              "generated codec context in the same assembly can reference it.",
                    Severity = JsonContractDiagnosticSeverity.Error,
                    OffendingType = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                }.WithLocation(location));
                continue;
            }

            var normalized = JsonContractRootNormalizer.Normalize(type);
            roots.Add(new JsonContractRootModel
            {
                RootType = normalized,
                FullMetadataName = normalized.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsExplicitExtra = true,
                Provenance = new JsonContractRootProvenance
                {
                    Origins =
                    [
                        new JsonContractRootOrigin
                        {
                            SourceKind = JsonContractRootSourceKind.LocalEvent,
                            DeclaringSurface = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            RoleName = "ReplayableLocalEvent",
                            Location = location,
                        },
                    ],
                },
            });
        }

        roots.Sort((left, right) => string.Compare(left.FullMetadataName, right.FullMetadataName, StringComparison.Ordinal));
        return roots;
    }
}
