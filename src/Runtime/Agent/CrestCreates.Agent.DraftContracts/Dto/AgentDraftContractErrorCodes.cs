namespace CrestCreates.Agent.DraftContracts.Dto;

public static class AgentDraftContractErrorCodes
{
    public const string NullPayload = "ADPC001";
    public const string DiscriminatorMismatch = "ADPC002";
    public const string UnsupportedKind = "ADPC003";
    public const string EmptyChangedFields = "ADPC004";
    public const string UnknownChangedField = "ADPC005";
    public const string MissingRequiredOnCreate = "ADPC006";
    public const string NonNullableFieldNull = "ADPC007";
    public const string CreateUnsupported = "ADPC008";
    public const string InvalidCollectionShape = "ADPC009";
    public const string PreserveStrategyFailed = "ADPC010";
    public const string UnrepresentableDomainShape = "ADPC011";
    public const string InvalidReferenceValue = "ADPC012";
}
