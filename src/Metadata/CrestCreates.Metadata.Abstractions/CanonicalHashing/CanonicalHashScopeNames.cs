namespace CrestCreates.Metadata.Abstractions.CanonicalHashing;

/// <summary>
/// Canonical string constants for <see cref="CanonicalHashScope"/> values.
/// Used in <see cref="CanonicalHash.Scope"/> and envelope metadata.
/// Never use enum.ToString() for hash input — always use these canonical string helpers.
/// </summary>
public static class CanonicalHashScopeNames
{
    public const string InternalFull = "InternalFull";
    public const string TenantVisible = "TenantVisible";
    public const string PublicCrossTenant = "PublicCrossTenant";

    /// <summary>
    /// Converts a <see cref="CanonicalHashScope"/> to its canonical string representation.
    /// </summary>
    public static string ToCanonicalString(CanonicalHashScope scope) => scope switch
    {
        CanonicalHashScope.InternalFull => InternalFull,
        CanonicalHashScope.TenantVisible => TenantVisible,
        CanonicalHashScope.PublicCrossTenant => PublicCrossTenant,
        _ => throw new System.ComponentModel.InvalidEnumArgumentException(nameof(scope), (int)scope, typeof(CanonicalHashScope))
    };
}
