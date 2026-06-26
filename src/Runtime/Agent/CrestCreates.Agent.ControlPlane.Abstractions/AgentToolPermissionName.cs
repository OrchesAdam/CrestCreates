using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public static class AgentToolPermissionNames
{
    public const string RuntimePrefixValue = "agent.runtime.";
    public static PermissionName RuntimePrefix { get; } = new(RuntimePrefixValue);

    public const string ContextReadValue = "agent.context.read";
    public static PermissionName ContextRead { get; } = new(ContextReadValue);

    public const string DescriptorReadValue = "agent.descriptor.read";
    public static PermissionName DescriptorRead { get; } = new(DescriptorReadValue);

    public const string DescriptorSearchValue = "agent.descriptor.search";
    public static PermissionName DescriptorSearch { get; } = new(DescriptorSearchValue);

    public const string DraftCreateValue = "agent.draft.create";
    public static PermissionName DraftCreate { get; } = new(DraftCreateValue);

    public const string DraftUpdateValue = "agent.draft.update";
    public static PermissionName DraftUpdate { get; } = new(DraftUpdateValue);

    public const string DraftReadValue = "agent.draft.read";
    public static PermissionName DraftRead { get; } = new(DraftReadValue);

    public const string DraftListValue = "agent.draft.list";
    public static PermissionName DraftList { get; } = new(DraftListValue);

    public const string DraftCancelValue = "agent.draft.cancel";
    public static PermissionName DraftCancel { get; } = new(DraftCancelValue);

    public const string ReviewValidateValue = "agent.review.validate";
    public static PermissionName ReviewValidate { get; } = new(ReviewValidateValue);

    public const string ReviewRunValue = "agent.review.run";
    public static PermissionName ReviewRun { get; } = new(ReviewRunValue);

    public const string ReviewReadValue = "agent.review.read";
    public static PermissionName ReviewRead { get; } = new(ReviewReadValue);

    public const string DiagnosticExplainValue = "agent.diagnostic.explain";
    public static PermissionName DiagnosticExplain { get; } = new(DiagnosticExplainValue);

    public const string FixSuggestValue = "agent.fix.suggest";
    public static PermissionName FixSuggest { get; } = new(FixSuggestValue);

    public const string FixApplyToDraftValue = "agent.fix.apply_to_draft";
    public static PermissionName FixApplyToDraft { get; } = new(FixApplyToDraftValue);

    public const string PackagePreviewValue = "agent.package.preview";
    public static PermissionName PackagePreview { get; } = new(PackagePreviewValue);

    public const string ActivationRequestSubmitValue = "agent.activation.request.submit";
    public static PermissionName ActivationRequestSubmit { get; } = new(ActivationRequestSubmitValue);

    public const string ActivationRequestReadValue = "agent.activation.request.read";
    public static PermissionName ActivationRequestRead { get; } = new(ActivationRequestReadValue);

    public const string ActivationRequestCancelValue = "agent.activation.request.cancel";
    public static PermissionName ActivationRequestCancel { get; } = new(ActivationRequestCancelValue);

    public const string ReviewReportBuildValue = "agent.review.report.build";
    public static PermissionName ReviewReportBuild { get; } = new(ReviewReportBuildValue);

    public const string ReviewReportRenderValue = "agent.review.report.render";
    public static PermissionName ReviewReportRender { get; } = new(ReviewReportRenderValue);
}
