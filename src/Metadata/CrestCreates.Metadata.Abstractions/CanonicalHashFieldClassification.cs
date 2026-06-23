namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Per-field classification for canonical hash profile declaration.
/// Determines which hash payloads include a given field.
/// </summary>
public enum CanonicalHashFieldClassification
{
    /// <summary>
    /// Included in both ContractHash and DefinitionHash payloads.
    /// Fields that affect invocation, execution, binding, or I/O structure.
    /// </summary>
    Contract = 1,

    /// <summary>
    /// Included in DefinitionHash only (not in ContractHash).
    /// Display metadata, labels, validation rules, layout, etc.
    /// </summary>
    DefinitionOnly = 2,

    /// <summary>
    /// Not included in any hash payload.
    /// Computed constants, hash outputs, or intentionally excluded with documented reason.
    /// </summary>
    Excluded = 3
}
