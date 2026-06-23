namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Canonical string constants for <see cref="CanonicalHashPurpose"/> values.
/// Used in <see cref="CanonicalHash.Purpose"/> and envelope metadata.
/// Never use enum.ToString() for hash input — always use these canonical string helpers.
/// </summary>
public static class CanonicalHashPurposeNames
{
    public const string Contract = "Contract";
    public const string Definition = "Definition";
    public const string SourceBinding = "SourceBinding";
    public const string Integrity = "Integrity";
    public const string AuditEvidence = "AuditEvidence";

    /// <summary>
    /// Converts a <see cref="CanonicalHashPurpose"/> to its canonical string representation.
    /// </summary>
    public static string ToCanonicalString(CanonicalHashPurpose purpose) => purpose switch
    {
        CanonicalHashPurpose.Contract => Contract,
        CanonicalHashPurpose.Definition => Definition,
        CanonicalHashPurpose.SourceBinding => SourceBinding,
        CanonicalHashPurpose.Integrity => Integrity,
        CanonicalHashPurpose.AuditEvidence => AuditEvidence,
        _ => throw new System.ComponentModel.InvalidEnumArgumentException(nameof(purpose), (int)purpose, typeof(CanonicalHashPurpose))
    };
}
