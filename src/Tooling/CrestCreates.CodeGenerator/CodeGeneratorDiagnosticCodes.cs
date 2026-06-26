// Analyzer package intentionally avoids referencing Core.Abstractions (netstandard2.0 target).
// Diagnostic ids are const-only here because Roslyn DiagnosticDescriptor requires string ids.
// Typed DiagnosticCode properties are not available in this project.
namespace CrestCreates.CodeGenerator;

public static class CodeGeneratorDiagnosticCodes
{
    public const string EntityGenerationErrorValue = "CCCG001";
    public const string ServiceGenerationErrorValue = "CCCG002";
    public const string GenerationErrorValue = "CCCG003";
    public const string AuthorizationConfigWarningValue = "CCCG004";
}
