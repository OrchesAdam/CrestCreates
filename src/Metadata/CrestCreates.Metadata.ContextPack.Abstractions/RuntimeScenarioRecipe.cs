namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record RuntimeScenarioRecipe
{
    public required string Name { get; init; }
    public required IReadOnlyList<ScenarioTraversalStep> Steps { get; init; }

    /// <summary>
    /// Deep copy for boundary snapshot isolation.
    /// </summary>
    public RuntimeScenarioRecipe Copy() => this with
    {
        Steps = Steps.ToArray()
    };
}
