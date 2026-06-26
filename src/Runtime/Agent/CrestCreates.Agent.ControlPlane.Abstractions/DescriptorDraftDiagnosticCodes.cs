using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public static class DescriptorDraftDiagnosticCodes
{
    private const string KindPayloadMismatchValue = "KIND_PAYLOAD_MISMATCH";
    public static DiagnosticCode KindPayloadMismatch { get; } = new(KindPayloadMismatchValue);

    private const string DraftIdEmptyValue = "DRAFT_ID_EMPTY";
    public static DiagnosticCode DraftIdEmpty { get; } = new(DraftIdEmptyValue);

    private const string DescriptorIdEmptyValue = "DESCRIPTOR_ID_EMPTY";
    public static DiagnosticCode DescriptorIdEmpty { get; } = new(DescriptorIdEmptyValue);

    private const string AuthorIdEmptyValue = "AUTHOR_ID_EMPTY";
    public static DiagnosticCode AuthorIdEmpty { get; } = new(AuthorIdEmptyValue);

    private const string RationaleEmptyValue = "RATIONALE_EMPTY";
    public static DiagnosticCode RationaleEmpty { get; } = new(RationaleEmptyValue);

    private const string IntentEmptyValue = "INTENT_EMPTY";
    public static DiagnosticCode IntentEmpty { get; } = new(IntentEmptyValue);

    private const string ProposedVersionMissingValue = "PROPOSED_VERSION_MISSING";
    public static DiagnosticCode ProposedVersionMissing { get; } = new(ProposedVersionMissingValue);

    private const string ProposedVersionNotIntegerValue = "PROPOSED_VERSION_NOT_INTEGER";
    public static DiagnosticCode ProposedVersionNotInteger { get; } = new(ProposedVersionNotIntegerValue);

    private const string ProposedVersionMismatchValue = "PROPOSED_VERSION_MISMATCH";
    public static DiagnosticCode ProposedVersionMismatch { get; } = new(ProposedVersionMismatchValue);

    private const string CreateBaseVersionMustBeEmptyValue = "CREATE_BASE_VERSION_MUST_BE_EMPTY";
    public static DiagnosticCode CreateBaseVersionMustBeEmpty { get; } = new(CreateBaseVersionMustBeEmptyValue);

    private const string UpdateBaseVersionRequiredValue = "UPDATE_BASE_VERSION_REQUIRED";
    public static DiagnosticCode UpdateBaseVersionRequired { get; } = new(UpdateBaseVersionRequiredValue);

    private const string PayloadIdMismatchValue = "PAYLOAD_ID_MISMATCH";
    public static DiagnosticCode PayloadIdMismatch { get; } = new(PayloadIdMismatchValue);
}
