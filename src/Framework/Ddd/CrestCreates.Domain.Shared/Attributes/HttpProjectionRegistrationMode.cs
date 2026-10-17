namespace CrestCreates.Domain.Shared.Attributes;

/// <summary>
/// How a hand-written AppService projection is activated on a host.
/// New declarations select <see cref="ExplicitHost"/>; the static registrar exists only for
/// assemblies that are being migrated by the legacy toolchain (#129 rev.2 §4.1/§14.4).
/// </summary>
public enum HttpProjectionRegistrationMode
{
    /// <summary>Registration happens through the per-assembly explicit-host activation.</summary>
    ExplicitHost = 0,

    /// <summary>Legacy process-global registration; retained only for the isolated legacy front-end.</summary>
    LegacyStatic = 1,
}
