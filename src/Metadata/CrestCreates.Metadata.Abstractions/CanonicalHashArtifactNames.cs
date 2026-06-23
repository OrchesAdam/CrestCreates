namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Canonical string constants for artifact kind values in canonical hash metadata.
/// Used in <see cref="CanonicalHash.ArtifactKind"/> and envelope metadata.
/// Never use enum.ToString() for hash input — always use these canonical string helpers.
/// </summary>
public static class CanonicalHashArtifactNames
{
    public const string Descriptor = "Descriptor";
    public const string ReviewResult = "ReviewResult";
    public const string Package = "Package";
    public const string Report = "Report";
}
