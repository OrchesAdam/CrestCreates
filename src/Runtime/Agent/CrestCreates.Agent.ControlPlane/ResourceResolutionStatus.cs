namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Status of a resource resolution attempt.
/// </summary>
internal enum ResourceResolutionStatus
{
    /// <summary>Resource was found and snapshot is available.</summary>
    Resolved,
    /// <summary>Resource was not found within the invocation tenant.</summary>
    NotFound,
    /// <summary>Multiple candidates found for an unpinned reference — caller must specify a version.</summary>
    Ambiguous
}
