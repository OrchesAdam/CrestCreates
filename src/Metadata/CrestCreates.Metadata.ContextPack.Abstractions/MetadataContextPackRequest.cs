using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackRequest
{
    public required MetadataContextPackScope Scope { get; init; }
    public required IReadOnlyList<DescriptorRef> FocusDescriptors { get; init; }
    public RuntimeScenarioRecipe? ScenarioRecipe { get; init; }
    public string? Intent { get; init; }
    public string? TenantId { get; init; }
    public IReadOnlyList<DescriptorKind>? IncludeKinds { get; init; }
    public IReadOnlyList<DescriptorKind>? ExcludeKinds { get; init; }
    public int MaxTraversalDepth { get; init; } = 2;
    public int MaxDescriptorCount { get; init; } = 64;
    public bool IncludeStableHashes { get; init; }
    public bool IncludeGovernanceState { get; init; }

    /// <summary>
    /// Deep copy for boundary snapshot isolation.
    /// </summary>
    public MetadataContextPackRequest Copy() => this with
    {
        FocusDescriptors = FocusDescriptors.ToArray(),
        ScenarioRecipe = ScenarioRecipe?.Copy(),
        IncludeKinds = IncludeKinds?.ToArray(),
        ExcludeKinds = ExcludeKinds?.ToArray()
    };
}
