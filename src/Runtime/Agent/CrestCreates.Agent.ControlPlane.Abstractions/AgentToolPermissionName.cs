namespace CrestCreates.Agent.ControlPlane.Abstractions;

public static class AgentToolPermissionName
{
    public const string ContextRead = "agent.context.read";
    public const string DescriptorRead = "agent.descriptor.read";
    public const string DescriptorSearch = "agent.descriptor.search";
    public const string DraftCreate = "agent.draft.create";
    public const string DraftUpdate = "agent.draft.update";
    public const string DraftRead = "agent.draft.read";
    public const string DraftList = "agent.draft.list";
    public const string DraftCancel = "agent.draft.cancel";
    public const string ReviewValidate = "agent.review.validate";
    public const string ReviewRun = "agent.review.run";
    public const string ReviewRead = "agent.review.read";
    public const string DiagnosticExplain = "agent.diagnostic.explain";
    public const string FixSuggest = "agent.fix.suggest";
    public const string FixApplyToDraft = "agent.fix.apply_to_draft";
    public const string PackagePreview = "agent.package.preview";
    public const string ActivationRequestSubmit = "agent.activation.request.submit";
    public const string ActivationRequestRead = "agent.activation.request.read";
    public const string ActivationRequestCancel = "agent.activation.request.cancel";
}
