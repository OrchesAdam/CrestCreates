using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Schema.Abstractions;

public static class SchemaValidationErrorCodes
{
    public static readonly DiagnosticCode UnknownProperty = new("UNKNOWN_PROPERTY");
    private const string FieldRequiredValue = "FIELD_REQUIRED";
    public static DiagnosticCode FieldRequired { get; } = new(FieldRequiredValue);

    private const string NullNotAllowedValue = "NULL_NOT_ALLOWED";
    public static DiagnosticCode NullNotAllowed { get; } = new(NullNotAllowedValue);

    private const string TypeMismatchValue = "TYPE_MISMATCH";
    public static DiagnosticCode TypeMismatch { get; } = new(TypeMismatchValue);

    private const string MaxLengthExceededValue = "MAX_LENGTH_EXCEEDED";
    public static DiagnosticCode MaxLengthExceeded { get; } = new(MaxLengthExceededValue);

    private const string MinLengthNotMetValue = "MIN_LENGTH_NOT_MET";
    public static DiagnosticCode MinLengthNotMet { get; } = new(MinLengthNotMetValue);

    private const string PatternMismatchValue = "PATTERN_MISMATCH";
    public static DiagnosticCode PatternMismatch { get; } = new(PatternMismatchValue);

    private const string MaxValueExceededValue = "MAX_VALUE_EXCEEDED";
    public static DiagnosticCode MaxValueExceeded { get; } = new(MaxValueExceededValue);

    private const string MinValueNotMetValue = "MIN_VALUE_NOT_MET";
    public static DiagnosticCode MinValueNotMet { get; } = new(MinValueNotMetValue);

    public static DiagnosticCode InvalidRoot { get; } = new("INVALID_ROOT");

    public static DiagnosticCode DuplicateProperty { get; } = new("DUPLICATE_PROPERTY");

    public static DiagnosticCode UnknownFieldType { get; } = new("UNKNOWN_FIELD_TYPE");
}
