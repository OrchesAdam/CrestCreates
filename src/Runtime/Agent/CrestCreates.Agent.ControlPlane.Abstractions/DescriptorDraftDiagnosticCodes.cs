using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public static class DescriptorDraftDiagnosticCodes
{
    public const string KindPayloadMismatchValue = "KIND_PAYLOAD_MISMATCH";
    public static DiagnosticCode KindPayloadMismatch { get; } = new(KindPayloadMismatchValue);

    public const string DraftIdEmptyValue = "DRAFT_ID_EMPTY";
    public static DiagnosticCode DraftIdEmpty { get; } = new(DraftIdEmptyValue);

    public const string DescriptorIdEmptyValue = "DESCRIPTOR_ID_EMPTY";
    public static DiagnosticCode DescriptorIdEmpty { get; } = new(DescriptorIdEmptyValue);

    public const string AuthorIdEmptyValue = "AUTHOR_ID_EMPTY";
    public static DiagnosticCode AuthorIdEmpty { get; } = new(AuthorIdEmptyValue);

    public const string RationaleEmptyValue = "RATIONALE_EMPTY";
    public static DiagnosticCode RationaleEmpty { get; } = new(RationaleEmptyValue);

    public const string IntentEmptyValue = "INTENT_EMPTY";
    public static DiagnosticCode IntentEmpty { get; } = new(IntentEmptyValue);

    public const string ProposedVersionMissingValue = "PROPOSED_VERSION_MISSING";
    public static DiagnosticCode ProposedVersionMissing { get; } = new(ProposedVersionMissingValue);

    public const string ProposedVersionNotIntegerValue = "PROPOSED_VERSION_NOT_INTEGER";
    public static DiagnosticCode ProposedVersionNotInteger { get; } = new(ProposedVersionNotIntegerValue);

    public const string ProposedVersionMismatchValue = "PROPOSED_VERSION_MISMATCH";
    public static DiagnosticCode ProposedVersionMismatch { get; } = new(ProposedVersionMismatchValue);

    public const string CreateBaseVersionMustBeEmptyValue = "CREATE_BASE_VERSION_MUST_BE_EMPTY";
    public static DiagnosticCode CreateBaseVersionMustBeEmpty { get; } = new(CreateBaseVersionMustBeEmptyValue);

    public const string UpdateBaseVersionRequiredValue = "UPDATE_BASE_VERSION_REQUIRED";
    public static DiagnosticCode UpdateBaseVersionRequired { get; } = new(UpdateBaseVersionRequiredValue);

    public const string PayloadIdMismatchValue = "PAYLOAD_ID_MISMATCH";
    public static DiagnosticCode PayloadIdMismatch { get; } = new(PayloadIdMismatchValue);
}
