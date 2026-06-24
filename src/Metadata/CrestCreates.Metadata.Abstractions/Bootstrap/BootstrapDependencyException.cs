namespace CrestCreates.Metadata.Abstractions.Bootstrap;

public sealed class BootstrapDependencyException : Exception
{
    public IReadOnlyList<string> Cycle { get; }

    public BootstrapDependencyException(IReadOnlyList<string> cycle)
        : base($"Bootstrap dependency cycle detected: {string.Join(" -> ", cycle)}")
    {
        Cycle = cycle;
    }
}
