using System;

namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Declares one field in a canonical hash profile.
/// Applied to the <c>Fields()</c> method inside a <see cref="CanonicalHashProfileAttribute"/>-marked class,
/// one attribute per field.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class CanonicalHashFieldAttribute : Attribute
{
    /// <summary>
    /// Creates a canonical hash field declaration.
    /// </summary>
    /// <param name="propertyName">The property name on the target type.</param>
    /// <param name="classification">How this field participates in hash computation.</param>
    public CanonicalHashFieldAttribute(string propertyName, CanonicalHashFieldClassification classification)
    {
        PropertyName = propertyName;
        Classification = classification;
    }

    /// <summary>
    /// The property name on the target type.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// How this field participates in hash computation.
    /// Single classification — the SG auto-derives Contract and Definition payloads.
    /// </summary>
    public CanonicalHashFieldClassification Classification { get; }

    /// <summary>
    /// Explicit ordering within the payload. If omitted, ordering follows attribute position.
    /// The SG re-sequences to continuous <c>[JsonPropertyOrder]</c> so segment gaps are not exposed.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Profile type for nested complex element types in collections.
    /// Required when the field is a collection of complex types.
    /// </summary>
    public Type? ElementProfile { get; init; }

    /// <summary>
    /// Profile type for nested complex value types (non-collection).
    /// Required when the field is a complex type but not a collection.
    /// </summary>
    public Type? ValueProfile { get; init; }

    /// <summary>
    /// How to handle dictionary/collection ordering for this field.
    /// </summary>
    public CanonicalHashCollectionOrderMode CollectionOrderMode { get; init; } = CanonicalHashCollectionOrderMode.None;

    /// <summary>
    /// Property name to sort by when <see cref="CollectionOrderMode"/> is <see cref="CanonicalHashCollectionOrderMode.OrdinalByProperty"/>.
    /// Required when CollectionOrderMode = OrdinalByProperty.
    /// </summary>
    public string? OrderByProperty { get; init; }

    /// <summary>
    /// Reason for the classification. Mandatory when <see cref="CanonicalHashFieldClassification.Excluded"/>.
    /// Optional for other classifications where the reason is self-explanatory.
    /// </summary>
    /// <remarks>
    /// Informational reason for the classification. Not consumed by the source generator.
    /// </remarks>
    public string? Reason { get; init; }

    /// <summary>
    /// Custom writer type for fields that require hand-written canonical JSON serialization logic.
    /// The type must have static methods: <c>WriteContractEnvelope(Utf8JsonWriter, TField, string, string, string)</c>
    /// and <c>WriteDefinitionEnvelope(Utf8JsonWriter, TField, string, string, string)</c>.
    /// Used for discriminated unions and other types the SG cannot handle automatically.
    /// When specified, the SG generates a call to this writer instead of inline serialization.
    /// </summary>
    public Type? CustomWriter { get; init; }
}
