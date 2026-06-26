using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public static class AgentToolDiagnosticCodes
{
    // Authorization
    public const string RuntimeExecutionDeniedValue = "RUNTIME_EXECUTION_DENIED";
    public static DiagnosticCode RuntimeExecutionDenied { get; } = new(RuntimeExecutionDeniedValue);

    public const string PermissionDeniedValue = "PERMISSION_DENIED";
    public static DiagnosticCode PermissionDenied { get; } = new(PermissionDeniedValue);

    public const string ToolDeniedValue = "TOOL_DENIED";
    public static DiagnosticCode ToolDenied { get; } = new(ToolDeniedValue);

    public const string ActorKindDeniedValue = "ACTOR_KIND_DENIED";
    public static DiagnosticCode ActorKindDenied { get; } = new(ActorKindDeniedValue);

    public const string UnknownAuthorizationModeValue = "UNKNOWN_AUTHORIZATION_MODE";
    public static DiagnosticCode UnknownAuthorizationMode { get; } = new(UnknownAuthorizationModeValue);

    public const string NotExplicitlyAllowedValue = "NOT_EXPLICITLY_ALLOWED";
    public static DiagnosticCode NotExplicitlyAllowed { get; } = new(NotExplicitlyAllowedValue);

    public const string MutationDeniedValue = "MUTATION_DENIED";
    public static DiagnosticCode MutationDenied { get; } = new(MutationDeniedValue);

    public const string ReadOnlyDeniedValue = "READ_ONLY_DENIED";
    public static DiagnosticCode ReadOnlyDenied { get; } = new(ReadOnlyDeniedValue);

    public const string AuthorizationUnresolvedValue = "AUTHORIZATION_UNRESOLVED";
    public static DiagnosticCode AuthorizationUnresolved { get; } = new(AuthorizationUnresolvedValue);

    // Tool invocation
    public const string ToolNameMismatchValue = "TOOL_NAME_MISMATCH";
    public static DiagnosticCode ToolNameMismatch { get; } = new(ToolNameMismatchValue);

    public const string ToolNotFoundValue = "TOOL_NOT_FOUND";
    public static DiagnosticCode ToolNotFound { get; } = new(ToolNotFoundValue);

    public const string ToolInvocationFailedValue = "TOOL_INVOCATION_FAILED";
    public static DiagnosticCode ToolInvocationFailed { get; } = new(ToolInvocationFailedValue);

    public const string ResultsSecurityTrimmedValue = "RESULTS_SECURITY_TRIMMED";
    public static DiagnosticCode ResultsSecurityTrimmed { get; } = new(ResultsSecurityTrimmedValue);

    // Search
    public const string DescriptorRefAmbiguousValue = "DESCRIPTOR_REF_AMBIGUOUS";
    public static DiagnosticCode DescriptorRefAmbiguous { get; } = new(DescriptorRefAmbiguousValue);

    public const string DescKindDeniedValue = "DESC_KIND_DENIED";
    public static DiagnosticCode DescKindDenied { get; } = new(DescKindDeniedValue);

    public const string SearchTruncatedValue = "SEARCH_TRUNCATED";
    public static DiagnosticCode SearchTruncated { get; } = new(SearchTruncatedValue);

    // Draft
    public const string DraftProjectionFailedValue = "DRAFT_PROJECTION_FAILED";
    public static DiagnosticCode DraftProjectionFailed { get; } = new(DraftProjectionFailedValue);

    // Review
    public const string NoReviewResultValue = "NO_REVIEW_RESULT";
    public static DiagnosticCode NoReviewResult { get; } = new(NoReviewResultValue);

    public const string UnsupportedReportContractVersionValue = "UNSUPPORTED_REPORT_CONTRACT_VERSION";
    public static DiagnosticCode UnsupportedReportContractVersion { get; } = new(UnsupportedReportContractVersionValue);

    public const string UnsupportedReportFormatValue = "UNSUPPORTED_REPORT_FORMAT";
    public static DiagnosticCode UnsupportedReportFormat { get; } = new(UnsupportedReportFormatValue);

    public const string ReviewValidationFailedValue = "REVIEW_VALIDATION_FAILED";
    public static DiagnosticCode ReviewValidationFailed { get; } = new(ReviewValidationFailedValue);

    public const string ValidationFailedValue = "VALIDATION_FAILED";
    public static DiagnosticCode ValidationFailed { get; } = new(ValidationFailedValue);

    public const string ReviewHasErrorsValue = "REVIEW_HAS_ERRORS";
    public static DiagnosticCode ReviewHasErrors { get; } = new(ReviewHasErrorsValue);

    public const string NotActivationEligibleValue = "NOT_ACTIVATION_ELIGIBLE";
    public static DiagnosticCode NotActivationEligible { get; } = new(NotActivationEligibleValue);

    // Fix proposal
    public const string ProposalDraftMismatchValue = "PROPOSAL_DRAFT_MISMATCH";
    public static DiagnosticCode ProposalDraftMismatch { get; } = new(ProposalDraftMismatchValue);

    public const string UnsupportedMultiActionFixProposalValue = "UNSUPPORTED_MULTI_ACTION_FIX_PROPOSAL";
    public static DiagnosticCode UnsupportedMultiActionFixProposal { get; } = new(UnsupportedMultiActionFixProposalValue);

    public const string FixProposalHasNoActionsValue = "FIX_PROPOSAL_HAS_NO_ACTIONS";
    public static DiagnosticCode FixProposalHasNoActions { get; } = new(FixProposalHasNoActionsValue);

    public const string FixProposalNotApplicableValue = "FIX_PROPOSAL_NOT_APPLICABLE";
    public static DiagnosticCode FixProposalNotApplicable { get; } = new(FixProposalNotApplicableValue);

    public const string NonExecutableFixActionValue = "NON_EXECUTABLE_FIX_ACTION";
    public static DiagnosticCode NonExecutableFixAction { get; } = new(NonExecutableFixActionValue);

    public const string UnsupportedFixActionKindValue = "UNSUPPORTED_FIX_ACTION_KIND";
    public static DiagnosticCode UnsupportedFixActionKind { get; } = new(UnsupportedFixActionKindValue);

    public const string UnsafeFixActionRejectedValue = "UNSAFE_FIX_ACTION_REJECTED";
    public static DiagnosticCode UnsafeFixActionRejected { get; } = new(UnsafeFixActionRejectedValue);

    public const string FixActionTargetBoundaryViolationValue = "FIX_ACTION_TARGET_BOUNDARY_VIOLATION";
    public static DiagnosticCode FixActionTargetBoundaryViolation { get; } = new(FixActionTargetBoundaryViolationValue);

    public const string FixActionTargetNotAllowedValue = "FIX_ACTION_TARGET_NOT_ALLOWED";
    public static DiagnosticCode FixActionTargetNotAllowed { get; } = new(FixActionTargetNotAllowedValue);

    public const string FixActionsAppliedValue = "FIX_ACTIONS_APPLIED";
    public static DiagnosticCode FixActionsApplied { get; } = new(FixActionsAppliedValue);

    // Runtime activation gate
    public const string RuntimeActivationGateRejectedValue = "RUNTIME_ACTIVATION_GATE_REJECTED";
    public static DiagnosticCode RuntimeActivationGateRejected { get; } = new(RuntimeActivationGateRejectedValue);

    // Diagnostic explanation
    public const string UnknownDiagnosticValue = "UNKNOWN_DIAGNOSTIC";
    public static DiagnosticCode UnknownDiagnostic { get; } = new(UnknownDiagnosticValue);
}
