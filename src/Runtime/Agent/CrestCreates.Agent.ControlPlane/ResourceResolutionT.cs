namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Result of a resource resolution attempt carrying either a snapshot or a status.
/// </summary>
internal sealed record ResourceResolution<T>(ResourceResolutionStatus Status, T? Snapshot)
    where T : class
{
    /// <summary>Creates a successful resolution with the given snapshot.</summary>
    public static ResourceResolution<T> Found(T snapshot) => new(ResourceResolutionStatus.Resolved, snapshot);

    /// <summary>Creates a not-found resolution.</summary>
    public static ResourceResolution<T> Missing() => new(ResourceResolutionStatus.NotFound, null);

    /// <summary>Creates an ambiguous resolution (multiple candidates for an unpinned ref).</summary>
    public static ResourceResolution<T> AmbiguousResult() => new(ResourceResolutionStatus.Ambiguous, null);
}
