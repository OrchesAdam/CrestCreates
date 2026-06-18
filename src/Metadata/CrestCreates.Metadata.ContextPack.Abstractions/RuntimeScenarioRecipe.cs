namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record RuntimeScenarioRecipe
{
    public required string Name { get; init; }
    public required IReadOnlyList<ScenarioTraversalStep> Steps { get; init; }
}
