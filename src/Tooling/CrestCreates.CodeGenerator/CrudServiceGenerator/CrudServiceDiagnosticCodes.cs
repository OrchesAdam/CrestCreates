// Analyzer package intentionally avoids referencing Core.Abstractions (netstandard2.0 target).
// Diagnostic ids are const-only here because Roslyn DiagnosticDescriptor requires string ids.
namespace CrestCreates.CodeGenerator.CrudServiceGenerator;

public static class CrudServiceDiagnosticCodes
{
    public const string InvalidDeclarationValue = "CCCRUD001";
    public const string PrecompileTransportMissingValue = "CCCRUD002";
    public const string IdentityConflictValue = "CCCRUD003";
    public const string StaticClosureUnsupportedValue = "CCCRUD004";
    public const string MissingRequiredReferenceValue = "CCCRUD005";
    public const string PrecompileInputCorruptedValue = "CCCRUD006";
    public const string FilterSortUnsupportedValue = "CCCRUD007";
    public const string LegacySerializationInconsistentValue = "CCCRUD008";
}
