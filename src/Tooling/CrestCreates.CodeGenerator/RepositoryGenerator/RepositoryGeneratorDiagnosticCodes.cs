namespace CrestCreates.CodeGenerator.RepositoryGenerator;

/// <summary>
/// #130 S1 freezes the diagnostic ids that S4 wires into the repository generators. Non-EF
/// generated repositories/CRUD are not part of the verified provider chain; silently emitting
/// nothing or falling back to EF instead of reporting one of these errors is a defect.
/// </summary>
public static class RepositoryGeneratorDiagnosticCodes
{
    /// <summary>
    /// A generated repository/CRUD was requested for a provider other than EF Core. Use the
    /// verified explicit asynchronous repository entry point for that provider instead.
    /// </summary>
    public const string NonEfProviderNotSupported = "CCCREPO001";

    /// <summary>
    /// The requested ORM provider is unknown. The generators must not silently default to EF Core.
    /// </summary>
    public const string UnknownProvider = "CCCREPO002";
}
