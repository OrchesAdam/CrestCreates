namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Controls how a versioned descriptor reference resolves when the target version is not specified exactly.
/// </summary>
public enum VersionSelectionMode
{
    /// <summary>
    /// Requires an exact version match. The reference must specify a specific version number.
    /// </summary>
    Exact,

    /// <summary>
    /// Resolves to the latest active version of the referenced descriptor.
    /// At runtime, inactive versions are excluded from resolution.
    /// </summary>
    Latest,

    /// <summary>
    /// Resolves to the latest version that is compatible with the referenced version,
    /// following semantic versioning compatibility rules.
    /// </summary>
    Compatible
}
