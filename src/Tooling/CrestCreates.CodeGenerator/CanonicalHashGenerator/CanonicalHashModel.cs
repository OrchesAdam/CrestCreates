using Microsoft.CodeAnalysis;
using System;
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

    // Resolved from TargetType's property symbol
    public required ITypeSymbol PropertyType { get; init; }
    public required bool IsNullable { get; init; }
    public required bool IsCollection { get; init; }
    public required bool IsDictionary { get; init; }

    // Location for diagnostics
    public required Location? Location { get; init; }

    /// <summary>
    /// Profile reference for collection element types (new model for union support).
    /// </summary>
    public ProfileReferenceModel? ElementProfileReference { get; init; }

    /// <summary>
    /// Profile reference for nested value types (new model for union support).
    /// </summary>
    public ProfileReferenceModel? ValueProfileReference { get; init; }

    /// <summary>
    /// Collection filter for this field. Only valid on collection-valued fields.
    /// </summary>
    public FieldFilterModel? Filter { get; init; }
}

/// <summary>
/// Models a reference to either a normal or union profile.
/// </summary>
internal sealed record ProfileReferenceModel
{
    public ProfileModel? NormalProfile { get; init; }
    public UnionProfileModel? UnionProfile { get; init; }
    public bool IsUnion => UnionProfile is not null;
}

/// <summary>
/// Models a collection filter on a profile field.
/// </summary>
internal sealed record FieldFilterModel
{
    public required INamedTypeSymbol FilterType { get; init; }
    public required ITypeSymbol ElementType { get; init; }
    public required string FullyQualifiedTypeName { get; init; }
}

/// <summary>
/// Models one case in a discriminated union profile.
/// </summary>
internal sealed record UnionCaseModel
{
    public required INamedTypeSymbol CaseType { get; init; }
    public required string DiscriminatorValue { get; init; }
    public required ProfileModel ValueProfile { get; init; }
    public required Location? Location { get; init; }
}

/// <summary>
/// Models a parsed canonical hash union profile class.
/// </summary>
internal sealed record UnionProfileModel
{
    public required string ProfileClassName { get; init; }
    public required INamedTypeSymbol ProfileClassSymbol { get; init; }
    public required INamedTypeSymbol TargetType { get; init; }
    public required string TargetTypeName { get; init; }
    public required string Discriminator { get; init; }
    public required IReadOnlyList<UnionCaseModel> Cases { get; init; }
    public required Location? Location { get; init; }

    public string Stem => ProfileClassName.EndsWith("CanonicalHashProfile")
        ? ProfileClassName.Substring(0, ProfileClassName.Length - "CanonicalHashProfile".Length)
        : ProfileClassName;
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
    /// When the same PropertyName appears in both Contract and DefinitionOnly,
    /// the DefinitionOnly entry takes precedence in DefinitionFields (the Contract
    /// entry is excluded). This supports patterns where ContractHash uses a
    /// filtered/reduced sub-profile while DefinitionHash uses the full profile
    /// for the same logical field.
    /// </summary>
    public IReadOnlyList<ProfileFieldModel> DefinitionFields =>
        Fields
            .Where(f => f.Classification != "Excluded")
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .Select(g => g.FirstOrDefault(f => f.Classification == "DefinitionOnly") ?? g.First())
            .OrderBy(f => f.Order)
            .ToList();

    /// <summary>
    /// Whether this profile represents a top-level descriptor type (has explicit DescriptorKind).
    /// Sub-structure profiles have DescriptorKind = "Unknown".
    /// </summary>
    public bool IsTopLevelDescriptor => DescriptorKind != "Unknown"
        && ArtifactKind == "Descriptor";
}
