namespace CrestCreates.Metadata.Abstractions.CanonicalHashing;

/// <summary>
/// What kind of artifact a canonical hash profile covers.
/// </summary>
public enum CanonicalHashArtifactKind
{
    /// <summary>
    /// A descriptor type (Schema, Capability, Event, Workflow, Form, HumanTask).
    /// </summary>
    Descriptor = 1,

    /// <summary>
    /// A review result. SG v1: reserved, reports CCHASH010.
    /// </summary>
    ReviewResult = 2,

    /// <summary>
    /// A descriptor package. SG v1: reserved.
    /// </summary>
    Package = 3,

    /// <summary>
    /// A review report. SG v1: reserved.
    /// </summary>
    Report = 4
}
