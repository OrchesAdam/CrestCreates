namespace CrestCreates.Metadata.Abstractions.CanonicalHashing;

/// <summary>
/// Controls how dictionary and collection fields are ordered in canonical hash payloads.
/// </summary>
public enum CanonicalHashCollectionOrderMode
{
    /// <summary>
    /// No ordering specified. Error if collection field has None — SG reports CCHASH003.
    /// </summary>
    None = 0,

    /// <summary>
    /// Preserve source order — reorder changes the hash.
    /// Used when element position is semantically meaningful (e.g., Workflow.Steps).
    /// </summary>
    SourceOrder = 1,

    /// <summary>
    /// Sort by value using string ordinal comparison.
    /// </summary>
    OrdinalByValue = 2,

    /// <summary>
    /// Sort by a specific property of elements.
    /// Requires <see cref="CanonicalHashFieldAttribute.OrderByProperty"/> to be set.
    /// </summary>
    OrdinalByProperty = 3,

    /// <summary>
    /// Dictionaries: canonicalize to ordered key-value list.
    /// Sort by key using string ordinal comparison.
    /// </summary>
    OrderedKeyValue = 4
}
