namespace CrestCreates.Schema;

public enum SchemaJsonContractViolation
{
    Unknown = 0,
    ValidationRulesUnsupported = 1,
    ReferencesUnsupported = 2,
    ScalarTypeUnsupported = 3,
    RootContractNotObject = 4,
    PatternUnsupported = 5,
    FieldIdentityInvalid = 6,
    LengthConstraintInvalid = 7,
    NumericConstraintInvalid = 8,
    FieldTypeMissing = 9,
    CollectionElementTypeMissing = 10,
    ConstraintNotApplicable = 11,
    IntegerConstraintInvalid = 12,
    JsonPropertyMismatch = 13,
    RequirednessMismatch = 14,
    NullabilityMismatch = 15,
    PropertyTypeMismatch = 16,
    AdditionalJsonProperty = 17
}

public sealed class SchemaJsonContractException : Exception
{
    public SchemaJsonContractException(
        SchemaJsonContractViolation violation,
        string message)
        : base(message)
    {
        Violation = violation;
    }

    public SchemaJsonContractViolation Violation { get; }
}
