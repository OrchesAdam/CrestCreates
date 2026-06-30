using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane;

/// <summary>
/// Deep-copies descriptors from the Company Certification control plane inventory.
/// Every copy is property-by-property (no reflection) for AOT compatibility.
/// All array/list properties are independently allocated so later mutations
/// cannot affect the original descriptors.
/// </summary>
public static class CompanyCertificationDescriptorCloner
{
    /// <summary>
    /// Returns a deep copy of every descriptor in the static Company Certification
    /// catalog. No returned object shares a reference with the static descriptors
    /// or with any other return value of this method.
    /// </summary>
    public static IReadOnlyList<IDescriptor> CopyAllDescriptors()
    {
        return CompanyCertificationDescriptorInventory.AllDescriptors()
            .Select(CopyDescriptor)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Dispatches to the per-kind copy method based on the concrete descriptor type.
    /// </summary>
    public static IDescriptor CopyDescriptor(IDescriptor descriptor) => descriptor switch
    {
        SchemaDescriptor d => CopySchema(d),
        FormDescriptor d => CopyForm(d),
        CapabilityDescriptor d => CopyCapability(d),
        EventDescriptor d => CopyEvent(d),
        HumanTaskDescriptor d => CopyHumanTask(d),
        WorkflowDescriptor d => CopyWorkflow(d),
        _ => throw new ArgumentException(
            $"Unknown descriptor type: {descriptor.GetType().FullName}", nameof(descriptor)),
    };

    // ── Schema ──────────────────────────────────────────────────────────

    public static SchemaDescriptor CopySchema(SchemaDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        Version = d.Version,
        ChangeKind = d.ChangeKind,
        Fields = d.Fields.Select(CopySchemaField).ToArray(),
        ValidationRules = d.ValidationRules.Select(CopyValidationRule).ToArray(),
        References = d.References.ToArray(),
    };

    public static SchemaFieldDescriptor CopySchemaField(SchemaFieldDescriptor f) => new()
    {
        Name = f.Name,
        FieldType = f.FieldType,
        IsRequired = f.IsRequired,
        IsNullable = f.IsNullable,
        MaxLength = f.MaxLength,
        MinLength = f.MinLength,
        MaxValue = f.MaxValue,
        MinValue = f.MinValue,
        Pattern = f.Pattern,
        IsCollection = f.IsCollection,
        CollectionElementType = f.CollectionElementType,
    };

    public static SchemaValidationRule CopyValidationRule(SchemaValidationRule r) => new()
    {
        Name = r.Name,
        Expression = r.Expression,
        ErrorMessage = r.ErrorMessage,
    };

    // ── Form ────────────────────────────────────────────────────────────

    public static FormDescriptor CopyForm(FormDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        Version = d.Version,
        Schema = d.Schema,
        Fields = d.Fields.Select(CopyFormField).ToArray(),
        LayoutColumns = d.LayoutColumns,
    };

    public static FormFieldDescriptor CopyFormField(FormFieldDescriptor f) => new()
    {
        SchemaFieldName = f.SchemaFieldName,
        Label = f.Label,
        Placeholder = f.Placeholder,
        HelpText = f.HelpText,
        FormatHint = f.FormatHint,
        Order = f.Order,
        Group = f.Group,
        IsReadOnly = f.IsReadOnly,
        VisibilityCondition = f.VisibilityCondition,
        ControlType = f.ControlType,
        IsRequiredOverride = f.IsRequiredOverride,
        ValidationMessage = f.ValidationMessage,
        DefaultValueExpression = f.DefaultValueExpression,
        OptionsSource = f.OptionsSource,
        Metadata = new Dictionary<string, string>(f.Metadata),
    };

    // ── Capability ──────────────────────────────────────────────────────

    public static CapabilityDescriptor CopyCapability(CapabilityDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        Version = d.Version,
        CapabilityKind = d.CapabilityKind,
        InputSchema = d.InputSchema,
        OutputSchema = d.OutputSchema,
        RiskLevel = d.RiskLevel,
        Categories = d.Categories.ToArray(),
        Produces = d.Produces.ToArray(),
        Consumes = d.Consumes.ToArray(),
        SemanticTags = d.SemanticTags.ToArray(),
        Permissions = d.Permissions.ToArray(),
    };

    // ── Event ───────────────────────────────────────────────────────────

    public static EventDescriptor CopyEvent(EventDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        Version = d.Version,
        PayloadSchema = d.PayloadSchema,
        Category = d.Category,
        Semantic = d.Semantic,
        Importance = d.Importance,
        ChangeKind = d.ChangeKind,
    };

    // ── HumanTask ───────────────────────────────────────────────────────

    public static HumanTaskDescriptor CopyHumanTask(HumanTaskDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        Version = d.Version,
        Interaction = d.Interaction,
        InputSchema = d.InputSchema,
        OutputSchema = d.OutputSchema,
        AssigneeStrategy = d.AssigneeStrategy,
        Timeout = d.Timeout,
        Permissions = d.Permissions,
        Outcomes = d.Outcomes.Select(CopyCompletionOutcome).ToArray(),
    };

    public static CompletionOutcome CopyCompletionOutcome(CompletionOutcome o) => new()
    {
        Condition = o.Condition,
        Capability = o.Capability,
    };

    // ── Workflow ────────────────────────────────────────────────────────

    public static WorkflowDescriptor CopyWorkflow(WorkflowDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        Version = d.Version,
        VariableSchema = d.VariableSchema,
        DefaultVariableScope = d.DefaultVariableScope,
        Steps = d.Steps.Select(CopyWorkflowStep).ToArray(),
    };

    /// <summary>
    /// Deep-copies a <see cref="WorkflowStep"/>, dispatching <see cref="WorkflowStep.Target"/>
    /// by its concrete <see cref="InteractionTarget"/> subtype.
    /// </summary>
    public static WorkflowStep CopyWorkflowStep(WorkflowStep s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Condition = s.Condition,
        InputMapping = s.InputMapping,
        OutputMapping = s.OutputMapping,
        OnError = s.OnError,
        Target = CopyInteractionTarget(s.Target),
        Transitions = s.Transitions.ToArray(),
    };

    private static InteractionTarget CopyInteractionTarget(InteractionTarget target) => target switch
    {
        CapabilityTarget ct => new CapabilityTarget
        {
            Capability = ct.Capability,
        },
        HumanTaskTarget ht => new HumanTaskTarget
        {
            HumanTask = ht.HumanTask,
        },
        SubWorkflowTarget sw => new SubWorkflowTarget
        {
            SubWorkflow = sw.SubWorkflow,
        },
        _ => throw new ArgumentException(
            $"Unknown InteractionTarget type: {target.GetType().FullName}", nameof(target)),
    };
}
