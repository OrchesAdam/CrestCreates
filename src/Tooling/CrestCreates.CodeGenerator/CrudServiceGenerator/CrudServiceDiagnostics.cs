using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.CrudServiceGenerator;

internal static class CrudServiceDiagnostics
{
    private const string Category = "CrudService";

    /// <summary>
    /// CCCRUD001: Formal CRUD declaration invalid — Attribute/entity/partial service/layout unsupported.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidDeclaration = new(
        id: CrudServiceDiagnosticCodes.InvalidDeclarationValue,
        title: "Invalid CRUD service declaration",
        messageFormat: "CRUD service declaration is invalid: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CCCRUD002: Precompile/JSON transport not assembled, actual output missing or version mismatch.
    /// </summary>
    public static readonly DiagnosticDescriptor PrecompileTransportMissing = new(
        id: CrudServiceDiagnosticCodes.PrecompileTransportMissingValue,
        title: "CRUD precompile transport missing",
        messageFormat: "CRUD precompile/JSON transport is not assembled: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CCCRUD003: Type/owner/route/descriptor identity conflict, duplicate artifacts.
    /// </summary>
    public static readonly DiagnosticDescriptor IdentityConflict = new(
        id: CrudServiceDiagnosticCodes.IdentityConflictValue,
        title: "CRUD identity conflict",
        messageFormat: "CRUD identity conflict: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CCCRUD004: DTO/closure/member/converter not statically supportable or inaccessible.
    /// </summary>
    public static readonly DiagnosticDescriptor StaticClosureUnsupported = new(
        id: CrudServiceDiagnosticCodes.StaticClosureUnsupportedValue,
        title: "CRUD static closure unsupported",
        messageFormat: "CRUD DTO/closure/member/converter cannot be statically supported: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CCCRUD005: Target project missing Application/HTTP required references or assembly ownership error.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingRequiredReference = new(
        id: CrudServiceDiagnosticCodes.MissingRequiredReferenceValue,
        title: "CRUD missing required references",
        messageFormat: "CRUD target project is missing required references: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CCCRUD006: Precompile input/path/write/incremental state corrupted.
    /// </summary>
    public static readonly DiagnosticDescriptor PrecompileInputCorrupted = new(
        id: CrudServiceDiagnosticCodes.PrecompileInputCorruptedValue,
        title: "CRUD precompile input corrupted",
        messageFormat: "CRUD precompile input/path/write/incremental state is corrupted: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CCCRUD007: Filter/sort configuration or field/operator type unsupported.
    /// </summary>
    public static readonly DiagnosticDescriptor FilterSortUnsupported = new(
        id: CrudServiceDiagnosticCodes.FilterSortUnsupportedValue,
        title: "CRUD filter/sort unsupported",
        messageFormat: "CRUD filter/sort configuration or field/operator type is unsupported: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CCCRUD008: Formal CRUD unexpectedly generates/consumes legacy serialization/provider, or JSON root inconsistent with action manifest.
    /// </summary>
    public static readonly DiagnosticDescriptor LegacySerializationInconsistent = new(
        id: CrudServiceDiagnosticCodes.LegacySerializationInconsistentValue,
        title: "CRUD legacy serialization or JSON root inconsistency",
        messageFormat: "CRUD unexpectedly uses legacy serialization or JSON root is inconsistent with action manifest: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
