namespace CrestCreates.Metadata.Abstractions.CanonicalHashing;

/// <summary>
/// Visibility boundary for canonical hash computation.
/// The scope determines which fields are included in the hash input.
/// </summary>
public enum CanonicalHashScope
{
    /// <summary>
    /// All fields, no filtering — for internal storage and governance.
    /// </summary>
    InternalFull = 1,

    /// <summary>
    /// Fields visible to the owning tenant (after denied-kind filtering).
    /// Used by agent/user-facing DTOs.
    /// </summary>
    TenantVisible = 2,

    /// <summary>
    /// Identity fields only — for cross-tenant deduplication without exposing internal structure.
    /// </summary>
    PublicCrossTenant = 3
}
