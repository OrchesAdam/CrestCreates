namespace CrestCreates.Agent.Tools;

public static class AgentToolStartupDiagnosticCodes
{
    public const string ActiveToolNameConflict = "ATP101";
    public const string DescriptorIdentityConflict = "ATP102";
    public const string InvalidDescriptorContract = "ATP103";
    public const string UnsupportedCapabilitySelection = "ATP104";
    public const string CapabilityResolutionFailure = "ATP105";
    public const string ExpectedContractHashMismatch = "ATP106";
    public const string SchemaReferenceNotExact = "ATP107";
    public const string InputSchemaTypeMismatch = "ATP108";
    public const string OutputSchemaTypeMismatch = "ATP109";
    public const string MissingBinding = "ATP110";
    public const string BindingMismatch = "ATP111";
    public const string MissingJsonTypeInfo = "ATP112";
    public const string InvalidJsonConfiguration = "ATP113";
    public const string JsonRootNotObject = "ATP114";
    public const string SchemaJsonParityFailure = "ATP115";
    public const string UnsupportedSchemaContract = "ATP116";
    public const string InvalidSideEffectClassification = "ATP117";
    public const string InvalidRiskFloor = "ATP118";
    public const string UnsafeGovernance = "ATP119";
    public const string MissingInvocationGate = "ATP120";
    public const string MissingInvocationLeaseAbandoner = "ATP126";
    public const string MissingApprovalGate = "ATP121";
    public const string MissingBudgetGate = "ATP122";
    public const string MissingGovernanceAuditor = "ATP123";
    public const string InvalidLifecycle = "ATP124";
    public const string SnapshotPublicationFailure = "ATP125";
    public const string DuplicateJsonContributor = "ATP127";
    public const string JsonContributorOrderConflict = "ATP128";
    public const string DuplicateJsonBindingRoot = "ATP129";
    public const string JsonContributorOptionsMismatch = "ATP130";
}
