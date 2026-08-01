namespace CrestCreates.Runtime.Persistence.Tests.Fixtures;

public sealed class MutableNestedRuntimeState
{
    public string Name { get; init; } = string.Empty;

    public List<string> Values { get; init; } = new();
}
