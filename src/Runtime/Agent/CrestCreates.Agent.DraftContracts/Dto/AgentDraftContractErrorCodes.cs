using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.DraftContracts.Dto;

public static class AgentDraftContractErrorCodes
{
    private const string NullPayloadValue = "ADPC001";
    public static DiagnosticCode NullPayload { get; } = new(NullPayloadValue);

    private const string DiscriminatorMismatchValue = "ADPC002";
    public static DiagnosticCode DiscriminatorMismatch { get; } = new(DiscriminatorMismatchValue);

    private const string UnsupportedKindValue = "ADPC003";
    public static DiagnosticCode UnsupportedKind { get; } = new(UnsupportedKindValue);

    private const string EmptyChangedFieldsValue = "ADPC004";
    public static DiagnosticCode EmptyChangedFields { get; } = new(EmptyChangedFieldsValue);

    private const string UnknownChangedFieldValue = "ADPC005";
    public static DiagnosticCode UnknownChangedField { get; } = new(UnknownChangedFieldValue);

    private const string MissingRequiredOnCreateValue = "ADPC006";
    public static DiagnosticCode MissingRequiredOnCreate { get; } = new(MissingRequiredOnCreateValue);

    private const string NonNullableFieldNullValue = "ADPC007";
    public static DiagnosticCode NonNullableFieldNull { get; } = new(NonNullableFieldNullValue);

    private const string CreateUnsupportedValue = "ADPC008";
    public static DiagnosticCode CreateUnsupported { get; } = new(CreateUnsupportedValue);

    private const string InvalidCollectionShapeValue = "ADPC009";
    public static DiagnosticCode InvalidCollectionShape { get; } = new(InvalidCollectionShapeValue);

    private const string PreserveStrategyFailedValue = "ADPC010";
    public static DiagnosticCode PreserveStrategyFailed { get; } = new(PreserveStrategyFailedValue);

    private const string UnrepresentableDomainShapeValue = "ADPC011";
    public static DiagnosticCode UnrepresentableDomainShape { get; } = new(UnrepresentableDomainShapeValue);

    private const string InvalidReferenceValueValue = "ADPC012";
    public static DiagnosticCode InvalidReferenceValue { get; } = new(InvalidReferenceValueValue);
}
