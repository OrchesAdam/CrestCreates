using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public static class AgentToolDiagnosticCodes
{
    // Authorization
    private const string RuntimeExecutionDeniedValue = "RUNTIME_EXECUTION_DENIED";
    public static DiagnosticCode RuntimeExecutionDenied { get; } = new(RuntimeExecutionDeniedValue);

    private const string PermissionDeniedValue = "PERMISSION_DENIED";
    public static DiagnosticCode PermissionDenied { get; } = new(PermissionDeniedValue);

    private const string ToolDeniedValue = "TOOL_DENIED";
    public static DiagnosticCode ToolDenied { get; } = new(ToolDeniedValue);

    private const string ActorKindDeniedValue = "ACTOR_KIND_DENIED";
    public static DiagnosticCode ActorKindDenied { get; } = new(ActorKindDeniedValue);

    private const string UnknownAuthorizationModeValue = "UNKNOWN_AUTHORIZATION_MODE";
    public static DiagnosticCode UnknownAuthorizationMode { get; } = new(UnknownAuthorizationModeValue);

    private const string NotExplicitlyAllowedValue = "NOT_EXPLICITLY_ALLOWED";
    public static DiagnosticCode NotExplicitlyAllowed { get; } = new(NotExplicitlyAllowedValue);

    private const string MutationDeniedValue = "MUTATION_DENIED";
    public static DiagnosticCode MutationDenied { get; } = new(MutationDeniedValue);

    private const string ReadOnlyDeniedValue = "READ_ONLY_DENIED";
    public static DiagnosticCode ReadOnlyDenied { get; } = new(ReadOnlyDeniedValue);

    private const string AuthorizationUnresolvedValue = "AUTHORIZATION_UNRESOLVED";
    public static DiagnosticCode AuthorizationUnresolved { get; } = new(AuthorizationUnresolvedValue);

    // Tool invocation
    private const string ToolNameMismatchValue = "TOOL_NAME_MISMATCH";
    public static DiagnosticCode ToolNameMismatch { get; } = new(ToolNameMismatchValue);

    private const string ToolNotFoundValue = "TOOL_NOT_FOUND";
    public static DiagnosticCode ToolNotFound { get; } = new(ToolNotFoundValue);

    private const string ToolInvocationFailedValue = "TOOL_INVOCATION_FAILED";
    public static DiagnosticCode ToolInvocationFailed { get; } = new(ToolInvocationFailedValue);

    private const string ResultsSecurityTrimmedValue = "RESULTS_SECURITY_TRIMMED";
    public static DiagnosticCode ResultsSecurityTrimmed { get; } = new(ResultsSecurityTrimmedValue);

    // Search
    private const string DescriptorRefAmbiguousValue = "DESCRIPTOR_REF_AMBIGUOUS";
    public static DiagnosticCode DescriptorRefAmbiguous { get; } = new(DescriptorRefAmbiguousValue);

    private const string DescKindDeniedValue = "DESC_KIND_DENIED";
    public static DiagnosticCode DescKindDenied { get; } = new(DescKindDeniedValue);

    private const string SearchTruncatedValue = "SEARCH_TRUNCATED";
    public static DiagnosticCode SearchTruncated { get; } = new(SearchTruncatedValue);

    // Draft
    private const string DraftProjectionFailedValue = "DRAFT_PROJECTION_FAILED";
    public static DiagnosticCode DraftProjectionFailed { get; } = new(DraftProjectionFailedValue);

    // Review
    private const string NoReviewResultValue = "NO_REVIEW_RESULT";
    public static DiagnosticCode NoReviewResult { get; } = new(NoReviewResultValue);

    private const string UnsupportedReportContractVersionValue = "UNSUPPORTED_REPORT_CONTRACT_VERSION";
    public static DiagnosticCode UnsupportedReportContractVersion { get; } = new(UnsupportedReportContractVersionValue);

    private const string UnsupportedReportFormatValue = "UNSUPPORTED_REPORT_FORMAT";
    public static DiagnosticCode UnsupportedReportFormat { get; } = new(UnsupportedReportFormatValue);

    private const string ReviewValidationFailedValue = "REVIEW_VALIDATION_FAILED";
    public static DiagnosticCode ReviewValidationFailed { get; } = new(ReviewValidationFailedValue);

    private const string ValidationFailedValue = "VALIDATION_FAILED";
    public static DiagnosticCode ValidationFailed { get; } = new(ValidationFailedValue);

    private const string ReviewHasErrorsValue = "REVIEW_HAS_ERRORS";
    public static DiagnosticCode ReviewHasErrors { get; } = new(ReviewHasErrorsValue);

    private const string NotActivationEligibleValue = "NOT_ACTIVATION_ELIGIBLE";
    public static DiagnosticCode NotActivationEligible { get; } = new(NotActivationEligibleValue);

    // Fix proposal
    private const string ProposalDraftMismatchValue = "PROPOSAL_DRAFT_MISMATCH";
    public static DiagnosticCode ProposalDraftMismatch { get; } = new(ProposalDraftMismatchValue);

    private const string UnsupportedMultiActionFixProposalValue = "UNSUPPORTED_MULTI_ACTION_FIX_PROPOSAL";
    public static DiagnosticCode UnsupportedMultiActionFixProposal { get; } = new(UnsupportedMultiActionFixProposalValue);

    private const string FixProposalHasNoActionsValue = "FIX_PROPOSAL_HAS_NO_ACTIONS";
    public static DiagnosticCode FixProposalHasNoActions { get; } = new(FixProposalHasNoActionsValue);

    private const string FixProposalNotApplicableValue = "FIX_PROPOSAL_NOT_APPLICABLE";
    public static DiagnosticCode FixProposalNotApplicable { get; } = new(FixProposalNotApplicableValue);

    private const string NonExecutableFixProposalValue = "NON_EXECUTABLE_FIX_PROPOSAL";
    public static DiagnosticCode NonExecutableFixProposal { get; } = new(NonExecutableFixProposalValue);

    private const string NonExecutableFixActionValue = "NON_EXECUTABLE_FIX_ACTION";
    public static DiagnosticCode NonExecutableFixAction { get; } = new(NonExecutableFixActionValue);

    private const string FixActionValueKindNotSupportedValue = "FIX_ACTION_VALUE_KIND_NOT_SUPPORTED";
    public static DiagnosticCode FixActionValueKindNotSupported { get; } = new(FixActionValueKindNotSupportedValue);

    private const string UnsupportedFixActionKindValue = "UNSUPPORTED_FIX_ACTION_KIND";
    public static DiagnosticCode UnsupportedFixActionKind { get; } = new(UnsupportedFixActionKindValue);

    private const string UnsafeFixActionRejectedValue = "UNSAFE_FIX_ACTION_REJECTED";
    public static DiagnosticCode UnsafeFixActionRejected { get; } = new(UnsafeFixActionRejectedValue);

    private const string FixActionTargetBoundaryViolationValue = "FIX_ACTION_TARGET_BOUNDARY_VIOLATION";
    public static DiagnosticCode FixActionTargetBoundaryViolation { get; } = new(FixActionTargetBoundaryViolationValue);

    private const string FixActionTargetNotAllowedValue = "FIX_ACTION_TARGET_NOT_ALLOWED";
    public static DiagnosticCode FixActionTargetNotAllowed { get; } = new(FixActionTargetNotAllowedValue);

    private const string FixActionsAppliedValue = "FIX_ACTIONS_APPLIED";
    public static DiagnosticCode FixActionsApplied { get; } = new(FixActionsAppliedValue);

    // Runtime activation gate
    private const string RuntimeActivationGateRejectedValue = "RUNTIME_ACTIVATION_GATE_REJECTED";
    public static DiagnosticCode RuntimeActivationGateRejected { get; } = new(RuntimeActivationGateRejectedValue);

    // Diagnostic explanation
    private const string UnknownDiagnosticValue = "UNKNOWN_DIAGNOSTIC";
    public static DiagnosticCode UnknownDiagnostic { get; } = new(UnknownDiagnosticValue);
}
