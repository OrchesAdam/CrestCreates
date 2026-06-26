using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Schema.Abstractions;

public static class SchemaValidationErrorCodes
{
    public const string FieldRequiredValue = "FIELD_REQUIRED";
    public static DiagnosticCode FieldRequired { get; } = new(FieldRequiredValue);

    public const string NullNotAllowedValue = "NULL_NOT_ALLOWED";
    public static DiagnosticCode NullNotAllowed { get; } = new(NullNotAllowedValue);

    public const string TypeMismatchValue = "TYPE_MISMATCH";
    public static DiagnosticCode TypeMismatch { get; } = new(TypeMismatchValue);

    public const string MaxLengthExceededValue = "MAX_LENGTH_EXCEEDED";
    public static DiagnosticCode MaxLengthExceeded { get; } = new(MaxLengthExceededValue);

    public const string MinLengthNotMetValue = "MIN_LENGTH_NOT_MET";
    public static DiagnosticCode MinLengthNotMet { get; } = new(MinLengthNotMetValue);

    public const string PatternMismatchValue = "PATTERN_MISMATCH";
    public static DiagnosticCode PatternMismatch { get; } = new(PatternMismatchValue);

    public const string MaxValueExceededValue = "MAX_VALUE_EXCEEDED";
    public static DiagnosticCode MaxValueExceeded { get; } = new(MaxValueExceededValue);

    public const string MinValueNotMetValue = "MIN_VALUE_NOT_MET";
    public static DiagnosticCode MinValueNotMet { get; } = new(MinValueNotMetValue);
}
