using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

public static class DescriptorActivationDiagnosticCodes
{
    public const string BindingSnapshotRequiredValue = "ACTIVATION_BINDING_SNAPSHOT_REQUIRED";
    public static DiagnosticCode BindingSnapshotRequired { get; } = new(BindingSnapshotRequiredValue);

    public const string BindingHashesRequiredValue = "ACTIVATION_BINDING_HASHES_REQUIRED";
    public static DiagnosticCode BindingHashesRequired { get; } = new(BindingHashesRequiredValue);

    public const string BlockedByGovernanceValue = "ACTIVATION_BLOCKED_BY_GOVERNANCE";
    public static DiagnosticCode BlockedByGovernance { get; } = new(BlockedByGovernanceValue);

    public const string IncompleteBindingValue = "ACTIVATION_INCOMPLETE_BINDING";
    public static DiagnosticCode IncompleteBinding { get; } = new(IncompleteBindingValue);

    public const string ReviewRequestMismatchValue = "ACTIVATION_REVIEW_REQUEST_MISMATCH";
    public static DiagnosticCode ReviewRequestMismatch { get; } = new(ReviewRequestMismatchValue);

    public const string ReviewDecisionMismatchValue = "ACTIVATION_REVIEW_DECISION_MISMATCH";
    public static DiagnosticCode ReviewDecisionMismatch { get; } = new(ReviewDecisionMismatchValue);

    public const string ReviewEvidenceMismatchValue = "ACTIVATION_REVIEW_EVIDENCE_MISMATCH";
    public static DiagnosticCode ReviewEvidenceMismatch { get; } = new(ReviewEvidenceMismatchValue);

    public const string ReviewEnvelopeMismatchValue = "ACTIVATION_REVIEW_ENVELOPE_MISMATCH";
    public static DiagnosticCode ReviewEnvelopeMismatch { get; } = new(ReviewEnvelopeMismatchValue);

    public const string InvalidStatusForApprovalValue = "ACTIVATION_INVALID_STATUS_FOR_APPROVAL";
    public static DiagnosticCode InvalidStatusForApproval { get; } = new(InvalidStatusForApprovalValue);

    public const string SelfApprovalForbiddenValue = "ACTIVATION_SELF_APPROVAL_FORBIDDEN";
    public static DiagnosticCode SelfApprovalForbidden { get; } = new(SelfApprovalForbiddenValue);

    public const string InvalidStatusForRejectionValue = "ACTIVATION_INVALID_STATUS_FOR_REJECTION";
    public static DiagnosticCode InvalidStatusForRejection { get; } = new(InvalidStatusForRejectionValue);

    public const string EvidenceStaleValue = "ACTIVATION_EVIDENCE_STALE";
    public static DiagnosticCode EvidenceStale { get; } = new(EvidenceStaleValue);

    public const string GateInvalidStateValue = "ACTIVATION_GATE_INVALID_STATE";
    public static DiagnosticCode GateInvalidState { get; } = new(GateInvalidStateValue);

    public const string GateBlockedValue = "ACTIVATION_GATE_BLOCKED";
    public static DiagnosticCode GateBlocked { get; } = new(GateBlockedValue);

    public const string CannotCancelValue = "ACTIVATION_CANNOT_CANCEL";
    public static DiagnosticCode CannotCancel { get; } = new(CannotCancelValue);

    public const string GovernanceBlockedValue = "ACTIVATION_GOVERNANCE_BLOCKED";
    public static DiagnosticCode GovernanceBlocked { get; } = new(GovernanceBlockedValue);

    public const string RequiresHumanReviewValue = "ACTIVATION_REQUIRES_HUMAN_REVIEW";
    public static DiagnosticCode RequiresHumanReview { get; } = new(RequiresHumanReviewValue);

    public const string ReviewNotRequiredValue = "ACTIVATION_REVIEW_NOT_REQUIRED";
    public static DiagnosticCode ReviewNotRequired { get; } = new(ReviewNotRequiredValue);

    public const string ReviewResultNotFoundValue = "ACTIVATION_REVIEW_RESULT_NOT_FOUND";
    public static DiagnosticCode ReviewResultNotFound { get; } = new(ReviewResultNotFoundValue);

    public const string ReviewResultDraftMismatchValue = "ACTIVATION_REVIEW_RESULT_DRAFT_MISMATCH";
    public static DiagnosticCode ReviewResultDraftMismatch { get; } = new(ReviewResultDraftMismatchValue);

    public const string PackagePreviewNotFoundValue = "ACTIVATION_PACKAGE_PREVIEW_NOT_FOUND";
    public static DiagnosticCode PackagePreviewNotFound { get; } = new(PackagePreviewNotFoundValue);

    public const string PackagePreviewDraftMismatchValue = "ACTIVATION_PACKAGE_PREVIEW_DRAFT_MISMATCH";
    public static DiagnosticCode PackagePreviewDraftMismatch { get; } = new(PackagePreviewDraftMismatchValue);

    public const string EvidencePreviewNotFoundValue = "ACTIVATION_EVIDENCE_PREVIEW_NOT_FOUND";
    public static DiagnosticCode EvidencePreviewNotFound { get; } = new(EvidencePreviewNotFoundValue);

    public const string EvidencePreviewDraftMismatchValue = "ACTIVATION_EVIDENCE_PREVIEW_DRAFT_MISMATCH";
    public static DiagnosticCode EvidencePreviewDraftMismatch { get; } = new(EvidencePreviewDraftMismatchValue);

    public const string HandoffDeniedValue = "ACTIVATION_HANDOFF_DENIED";
    public static DiagnosticCode HandoffDenied { get; } = new(HandoffDeniedValue);

    public const string BindingHashValidationFailedValue = "ACTIVATION_BINDING_HASH_VALIDATION_FAILED";
    public static DiagnosticCode BindingHashValidationFailed { get; } = new(BindingHashValidationFailedValue);
}
