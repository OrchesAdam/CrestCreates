using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public static class AgentToolPermissionNames
{
    private const string RuntimePrefixValue = "agent.runtime.";
    public static PermissionName RuntimePrefix { get; } = new(RuntimePrefixValue);

    private const string ContextReadValue = "agent.context.read";
    public static PermissionName ContextRead { get; } = new(ContextReadValue);

    private const string DescriptorReadValue = "agent.descriptor.read";
    public static PermissionName DescriptorRead { get; } = new(DescriptorReadValue);

    private const string DescriptorSearchValue = "agent.descriptor.search";
    public static PermissionName DescriptorSearch { get; } = new(DescriptorSearchValue);

    private const string DraftCreateValue = "agent.draft.create";
    public static PermissionName DraftCreate { get; } = new(DraftCreateValue);

    private const string DraftUpdateValue = "agent.draft.update";
    public static PermissionName DraftUpdate { get; } = new(DraftUpdateValue);

    private const string DraftReadValue = "agent.draft.read";
    public static PermissionName DraftRead { get; } = new(DraftReadValue);

    private const string DraftListValue = "agent.draft.list";
    public static PermissionName DraftList { get; } = new(DraftListValue);

    private const string DraftCancelValue = "agent.draft.cancel";
    public static PermissionName DraftCancel { get; } = new(DraftCancelValue);

    private const string ReviewValidateValue = "agent.review.validate";
    public static PermissionName ReviewValidate { get; } = new(ReviewValidateValue);

    private const string ReviewRunValue = "agent.review.run";
    public static PermissionName ReviewRun { get; } = new(ReviewRunValue);

    private const string ReviewReadValue = "agent.review.read";
    public static PermissionName ReviewRead { get; } = new(ReviewReadValue);

    private const string DiagnosticExplainValue = "agent.diagnostic.explain";
    public static PermissionName DiagnosticExplain { get; } = new(DiagnosticExplainValue);

    private const string FixSuggestValue = "agent.fix.suggest";
    public static PermissionName FixSuggest { get; } = new(FixSuggestValue);

    private const string FixApplyToDraftValue = "agent.fix.apply_to_draft";
    public static PermissionName FixApplyToDraft { get; } = new(FixApplyToDraftValue);

    private const string PackagePreviewValue = "agent.package.preview";
    public static PermissionName PackagePreview { get; } = new(PackagePreviewValue);

    private const string ActivationRequestSubmitValue = "agent.activation.request.submit";
    public static PermissionName ActivationRequestSubmit { get; } = new(ActivationRequestSubmitValue);

    private const string ActivationRequestReadValue = "agent.activation.request.read";
    public static PermissionName ActivationRequestRead { get; } = new(ActivationRequestReadValue);

    private const string ActivationRequestCancelValue = "agent.activation.request.cancel";
    public static PermissionName ActivationRequestCancel { get; } = new(ActivationRequestCancelValue);

    private const string ReviewReportBuildValue = "agent.review.report.build";
    public static PermissionName ReviewReportBuild { get; } = new(ReviewReportBuildValue);

    private const string ReviewReportRenderValue = "agent.review.report.render";
    public static PermissionName ReviewReportRender { get; } = new(ReviewReportRenderValue);
}
