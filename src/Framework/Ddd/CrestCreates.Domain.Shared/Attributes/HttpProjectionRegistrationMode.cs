namespace CrestCreates.Domain.Shared.Attributes;

/// <summary>
/// How a hand-written AppService projection is activated on a host.
/// Formal declarations select <see cref="ExplicitHost"/>; the static registrar was retired with the
/// legacy Dynamic API (#129 S4 exit).
/// </summary>
public enum HttpProjectionRegistrationMode
{
    /// <summary>Registration happens through the per-assembly explicit-host activation.</summary>
    ExplicitHost = 0,

    /// <summary>Retired with the legacy Dynamic API; declarations using it fail with CCHTTP001.</summary>
    [System.Obsolete("RegistrationMode.LegacyStatic is retired; use HttpProjectionRegistrationMode.ExplicitHost.")]
    LegacyStatic = 1,
}
