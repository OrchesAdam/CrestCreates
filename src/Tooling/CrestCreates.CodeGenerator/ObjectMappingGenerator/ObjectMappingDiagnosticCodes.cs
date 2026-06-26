namespace CrestCreates.CodeGenerator.ObjectMappingGenerator;

public static class ObjectMappingDiagnosticCodes
{
    public const string SourceTypeNotFoundValue = "OM100";
    public const string TargetTypeNotFoundValue = "OM101";
    public const string SourcePropertyNotFoundValue = "OM002";
    public const string TargetPropertyNotMappedValue = "OM001";
    public const string TypeIncompatibilityValue = "OM004";
    public const string ReadOnlyTargetValue = "OM006";
    public const string AmbiguousMappingValue = "OM003";
    public const string MissingElementMappingValue = "OM007";
    public const string ProtectedInputFieldWriteSkippedValue = "OM009";
    public const string NullabilityMismatchValue = "OM005";
    public const string NavigationPathInvalidValue = "OM008";
    public const string InvalidConverterTypeValue = "OM012";
}
