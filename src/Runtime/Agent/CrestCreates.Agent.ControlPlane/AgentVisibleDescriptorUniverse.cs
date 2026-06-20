using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Result of attempting to create a <see cref="AgentVisibleDescriptorUniverse"/>.
/// Returns <see cref="Created"/> on success, or <see cref="InvalidKindDetected"/>
/// when the catalog contains an undefined <see cref="DescriptorKind"/> value.
/// </summary>
internal sealed class UniverseCreationResult
{
    public AgentVisibleDescriptorUniverse? Universe { get; }
    public bool IsSuccess { get; }

    private UniverseCreationResult(AgentVisibleDescriptorUniverse? universe, bool isSuccess)
    {
        Universe = universe;
        IsSuccess = isSuccess;
    }

    public static UniverseCreationResult Created(AgentVisibleDescriptorUniverse universe) => new(universe, true);
    public static UniverseCreationResult InvalidKindDetected() => new(null, false);
}

/// <summary>
/// Immutable snapshot of a complete catalog, classified into all
/// tenant descriptors and only those visible under an invocation scope.
///
/// Broad aggregate, graph/topology, and context-pack operations all
/// derive their working universe from a single instance, guaranteeing
/// that denied descriptors never appear in downstream results.
/// </summary>
internal sealed record AgentVisibleDescriptorUniverse(
    IReadOnlyList<IDescriptor> AllTenantDescriptors,
    IReadOnlyList<IDescriptor> VisibleDescriptors)
{
    /// <summary>
    /// Creates a classified universe from a raw catalog source.
    /// Returns <see cref="UniverseCreationResult.InvalidKindDetected"/> when
    /// the catalog contains an undefined <see cref="DescriptorKind"/> value,
    /// consistent with the aggregate failure pattern used elsewhere.
    /// </summary>
    public static UniverseCreationResult TryCreate(
        IEnumerable<IDescriptor> source,
        AgentDescriptorVisibilityScope scope)
    {
        var all = source.ToList().AsReadOnly();

        if (all.Any(d => !AgentDescriptorKindPolicyEvaluator.IsValidDescriptorKind(d.Kind)))
            return UniverseCreationResult.InvalidKindDetected();

        return UniverseCreationResult.Created(new AgentVisibleDescriptorUniverse(
            all, scope.Filter(all, d => d.Kind)));
    }
}
