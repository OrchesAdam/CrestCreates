namespace CrestCreates.Metadata.Abstractions.Bootstrap;

public sealed class BootstrapDependencyException : Exception
{
    public IReadOnlyList<string> Cycle { get; }
    public string? MissingDependency { get; }
    public string? DuplicateTaskId { get; }

    public BootstrapDependencyException(IReadOnlyList<string> cycle)
        : base($"Bootstrap dependency cycle detected: {string.Join(" -> ", cycle)}")
    {
        Cycle = cycle;
    }

    public BootstrapDependencyException(string taskId, string missingDependency)
        : base($"Bootstrap task '{taskId}' depends on missing task '{missingDependency}'.")
    {
        Cycle = Array.Empty<string>();
        MissingDependency = missingDependency;
    }

    public static BootstrapDependencyException ForDuplicate(string taskId)
        => new(taskId, taskId, duplicate: true);

    private BootstrapDependencyException(string taskId, string duplicateTaskId, bool duplicate)
        : base($"Bootstrap task ID '{taskId}' is registered more than once.")
    {
        Cycle = Array.Empty<string>();
        DuplicateTaskId = duplicateTaskId;
    }
}
