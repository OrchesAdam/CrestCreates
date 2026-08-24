using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

public static class DescriptorActivationDiagnosticCodes
{
    private const string BindingSnapshotRequiredValue = "ACTIVATION_BINDING_SNAPSHOT_REQUIRED";
    public static DiagnosticCode BindingSnapshotRequired { get; } = new(BindingSnapshotRequiredValue);

    private const string BindingHashesRequiredValue = "ACTIVATION_BINDING_HASHES_REQUIRED";
    public static DiagnosticCode BindingHashesRequired { get; } = new(BindingHashesRequiredValue);

    private const string BlockedByGovernanceValue = "ACTIVATION_BLOCKED_BY_GOVERNANCE";
    public static DiagnosticCode BlockedByGovernance { get; } = new(BlockedByGovernanceValue);

    private const string IncompleteBindingValue = "ACTIVATION_INCOMPLETE_BINDING";
    public static DiagnosticCode IncompleteBinding { get; } = new(IncompleteBindingValue);

    private const string ReviewRequestMismatchValue = "ACTIVATION_REVIEW_REQUEST_MISMATCH";
    public static DiagnosticCode ReviewRequestMismatch { get; } = new(ReviewRequestMismatchValue);

    private const string ReviewDecisionMismatchValue = "ACTIVATION_REVIEW_DECISION_MISMATCH";
    public static DiagnosticCode ReviewDecisionMismatch { get; } = new(ReviewDecisionMismatchValue);

    private const string ReviewEvidenceMismatchValue = "ACTIVATION_REVIEW_EVIDENCE_MISMATCH";
    public static DiagnosticCode ReviewEvidenceMismatch { get; } = new(ReviewEvidenceMismatchValue);

    private const string ReviewEnvelopeMismatchValue = "ACTIVATION_REVIEW_ENVELOPE_MISMATCH";
    public static DiagnosticCode ReviewEnvelopeMismatch { get; } = new(ReviewEnvelopeMismatchValue);

    private const string InvalidStatusForApprovalValue = "ACTIVATION_INVALID_STATUS_FOR_APPROVAL";
    public static DiagnosticCode InvalidStatusForApproval { get; } = new(InvalidStatusForApprovalValue);

    private const string SelfApprovalForbiddenValue = "ACTIVATION_SELF_APPROVAL_FORBIDDEN";
    public static DiagnosticCode SelfApprovalForbidden { get; } = new(SelfApprovalForbiddenValue);

    private const string InvalidStatusForRejectionValue = "ACTIVATION_INVALID_STATUS_FOR_REJECTION";
    public static DiagnosticCode InvalidStatusForRejection { get; } = new(InvalidStatusForRejectionValue);

    private const string EvidenceStaleValue = "ACTIVATION_EVIDENCE_STALE";
    public static DiagnosticCode EvidenceStale { get; } = new(EvidenceStaleValue);

    private const string GateInvalidStateValue = "ACTIVATION_GATE_INVALID_STATE";
    public static DiagnosticCode GateInvalidState { get; } = new(GateInvalidStateValue);

    private const string GateBlockedValue = "ACTIVATION_GATE_BLOCKED";
    public static DiagnosticCode GateBlocked { get; } = new(GateBlockedValue);

    private const string CannotCancelValue = "ACTIVATION_CANNOT_CANCEL";
    public static DiagnosticCode CannotCancel { get; } = new(CannotCancelValue);

    private const string GovernanceBlockedValue = "ACTIVATION_GOVERNANCE_BLOCKED";
    public static DiagnosticCode GovernanceBlocked { get; } = new(GovernanceBlockedValue);

    private const string RequiresHumanReviewValue = "ACTIVATION_REQUIRES_HUMAN_REVIEW";
    public static DiagnosticCode RequiresHumanReview { get; } = new(RequiresHumanReviewValue);

    private const string ReviewNotRequiredValue = "ACTIVATION_REVIEW_NOT_REQUIRED";
    public static DiagnosticCode ReviewNotRequired { get; } = new(ReviewNotRequiredValue);

    private const string ReviewResultNotFoundValue = "ACTIVATION_REVIEW_RESULT_NOT_FOUND";
    public static DiagnosticCode ReviewResultNotFound { get; } = new(ReviewResultNotFoundValue);

    private const string ReviewResultDraftMismatchValue = "ACTIVATION_REVIEW_RESULT_DRAFT_MISMATCH";
    public static DiagnosticCode ReviewResultDraftMismatch { get; } = new(ReviewResultDraftMismatchValue);

    private const string ReviewDuplicateValue = "ACTIVATION_REVIEW_DUPLICATE";
    public static DiagnosticCode ReviewDuplicate { get; } = new(ReviewDuplicateValue);

    private const string ReviewConflictValue = "ACTIVATION_REVIEW_CONFLICT";
    public static DiagnosticCode ReviewConflict { get; } = new(ReviewConflictValue);

    private const string PackagePreviewNotFoundValue = "ACTIVATION_PACKAGE_PREVIEW_NOT_FOUND";
    public static DiagnosticCode PackagePreviewNotFound { get; } = new(PackagePreviewNotFoundValue);

    private const string PackagePreviewDraftMismatchValue = "ACTIVATION_PACKAGE_PREVIEW_DRAFT_MISMATCH";
    public static DiagnosticCode PackagePreviewDraftMismatch { get; } = new(PackagePreviewDraftMismatchValue);

    private const string EvidencePreviewNotFoundValue = "ACTIVATION_EVIDENCE_PREVIEW_NOT_FOUND";
    public static DiagnosticCode EvidencePreviewNotFound { get; } = new(EvidencePreviewNotFoundValue);

    private const string EvidencePreviewDraftMismatchValue = "ACTIVATION_EVIDENCE_PREVIEW_DRAFT_MISMATCH";
    public static DiagnosticCode EvidencePreviewDraftMismatch { get; } = new(EvidencePreviewDraftMismatchValue);

    private const string HandoffDeniedValue = "ACTIVATION_HANDOFF_DENIED";
    public static DiagnosticCode HandoffDenied { get; } = new(HandoffDeniedValue);

    private const string BindingHashValidationFailedValue = "ACTIVATION_BINDING_HASH_VALIDATION_FAILED";
    public static DiagnosticCode BindingHashValidationFailed { get; } = new(BindingHashValidationFailedValue);
}
