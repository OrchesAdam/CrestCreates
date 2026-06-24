using System;

namespace CrestCreates.Metadata.Abstractions.CanonicalHashing;

/// <summary>
/// Marks a class as a canonical hash profile declaration container.
/// The class should contain exactly one method carrying <see cref="CanonicalHashFieldAttribute"/> declarations.
/// The Source Generator reads these attributes at compile time and generates
/// Contract/Definition payloads, projections, dispatcher, and JsonContext.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CanonicalHashProfileAttribute : Attribute
{
    /// <summary>
    /// What kind of artifact this profile covers.
    /// </summary>
    public required CanonicalHashArtifactKind ArtifactKind { get; init; }

    /// <summary>
    /// The <see cref="Abstractions.DescriptorKind"/> this profile covers.
    /// Must be explicitly set when <see cref="ArtifactKind"/> is <see cref="CanonicalHashArtifactKind.Descriptor"/>.
    /// All runtime switches must reject <c>DescriptorKind.Unknown</c>.
    /// </summary>
    public DescriptorKind DescriptorKind { get; init; } = DescriptorKind.Unknown;

    /// <summary>
    /// The target type this profile describes (e.g., <c>typeof(SchemaDescriptor)</c>).
    /// </summary>
    public required Type TargetType { get; init; }

    /// <summary>
    /// The canonical shape version string for ContractHash (e.g., "schema-contract-hash-v1").
    /// Bumped when the Contract field set or ordering changes.
    /// </summary>
    public required string ContractShapeVersion { get; init; }

    /// <summary>
    /// The canonical shape version string for DefinitionHash (e.g., "schema-definition-hash-v1").
    /// Bumped when the Definition field set or ordering changes.
    /// </summary>
    public required string DefinitionShapeVersion { get; init; }
}
