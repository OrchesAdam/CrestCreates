using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace CrestCreates.CodeGenerator.CanonicalHashGenerator;

/// <summary>
/// Models one field declaration from a [CanonicalHashField] attribute.
/// </summary>
internal sealed record ProfileFieldModel
{
    public required string PropertyName { get; init; }
    public required string Classification { get; init; }  // "Contract", "DefinitionOnly", "Excluded"
    public required int Order { get; init; }
    public required string CollectionOrderMode { get; init; }  // "None", "SourceOrder", "OrdinalByValue", "OrdinalByProperty", "OrderedKeyValue"
    public string? OrderByProperty { get; init; }
    public ProfileModel? ElementProfile { get; init; }
    public ProfileModel? ValueProfile { get; init; }
    public string? Reason { get; init; }

    /// <summary>
    /// Fully-qualified type name of a custom writer class for fields requiring hand-written
    /// canonical JSON serialization (e.g., discriminated unions).
    /// When set, the SG generates a call to this writer instead of inline serialization.
    /// </summary>
    public string? CustomWriterTypeName { get; init; }

    // Resolved from TargetType's property symbol
    public required ITypeSymbol PropertyType { get; init; }
    public required bool IsNullable { get; init; }
    public required bool IsCollection { get; init; }
    public required bool IsDictionary { get; init; }

    // Location for diagnostics
    public required Location? Location { get; init; }
}

/// <summary>
/// Models a parsed canonical hash profile class.
/// </summary>
internal sealed record ProfileModel
{
    public required string ProfileClassName { get; init; }
    public required INamedTypeSymbol ProfileClassSymbol { get; init; }
    public required string TargetTypeName { get; init; }
    public required string ArtifactKind { get; init; }  // "Descriptor", "ReviewResult", "Package", "Report"
    public required string DescriptorKind { get; init; }  // "Unknown", "Schema", "Capability", etc.
    public required INamedTypeSymbol TargetType { get; init; }
    public required string ContractShapeVersion { get; init; }
    public required string DefinitionShapeVersion { get; init; }
    public required IReadOnlyList<ProfileFieldModel> Fields { get; set; }

    // Location for diagnostics
    public required Location? Location { get; init; }

    /// <summary>
    /// Profile class name stem (without "CanonicalHashProfile" suffix).
    /// Used for generated type naming.
    /// </summary>
    public string Stem => ProfileClassName.EndsWith("CanonicalHashProfile")
        ? ProfileClassName.Substring(0, ProfileClassName.Length - "CanonicalHashProfile".Length)
        : ProfileClassName;

    /// <summary>
    /// Fields whose classification is "Contract".
    /// </summary>
    public IReadOnlyList<ProfileFieldModel> ContractFields =>
        Fields.Where(f => f.Classification == "Contract").OrderBy(f => f.Order).ToList();

    /// <summary>
    /// Fields whose classification is "Contract" or "DefinitionOnly" (i.e., not "Excluded").
    /// Contract fields come first, then DefinitionOnly fields.
    /// </summary>
    public IReadOnlyList<ProfileFieldModel> DefinitionFields =>
        Fields.Where(f => f.Classification != "Excluded").OrderBy(f => f.Order).ToList();

    /// <summary>
    /// Whether this profile represents a top-level descriptor type (has explicit DescriptorKind).
    /// Sub-structure profiles have DescriptorKind = "Unknown".
    /// </summary>
    public bool IsTopLevelDescriptor => DescriptorKind != "Unknown"
        && ArtifactKind == "Descriptor";
}
