using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

using GeneratedEventDescriptor = CrestCreates.Event.Abstractions.GeneratedEventDescriptor;

namespace CrestCreates.DescriptorDraft;

/// <summary>
/// Internal Phase 7a-local proposed inventory snapshot helper.
/// Clones descriptors and their mutable collection state so the proposed
/// inventory does not share references with currentInventory or draft payload.
/// <para>
/// This is temporary until #35 (ISnapshotable adoption across boundary models).
/// Do not use outside of Phase 7a materialization.
/// </para>
/// </summary>
internal static class DescriptorDraftSnapshotHelper
{
    public static IReadOnlyList<IDescriptor> SnapshotInventory(
        IReadOnlyList<IDescriptor> inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return inventory.Select(SnapshotDescriptor).ToArray();
    }

    public static IDescriptor SnapshotDescriptor(IDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.Kind switch
        {
            DescriptorKind.Schema when descriptor is SchemaDescriptor schema =>
                SnapshotSchema(schema),
            DescriptorKind.Form when descriptor is FormDescriptor form =>
                SnapshotForm(form),
            DescriptorKind.Capability when descriptor is CapabilityDescriptor capability =>
                SnapshotCapability(capability),
            DescriptorKind.HumanTask when descriptor is HumanTaskDescriptor humanTask =>
                SnapshotHumanTask(humanTask),
            DescriptorKind.Workflow when descriptor is WorkflowDescriptor workflow =>
                SnapshotWorkflow(workflow),
            DescriptorKind.Event when descriptor is EventDescriptor @event =>
                SnapshotEvent(@event),
            DescriptorKind.Event when descriptor is GeneratedEventDescriptor generated =>
                SnapshotGeneratedEvent(generated),
            _ => throw new NotSupportedException(
                $"Unsupported descriptor kind: {descriptor.Kind} or type mismatch with CLR type {descriptor.GetType().FullName}")
        };
    }

    private static SchemaDescriptor SnapshotSchema(SchemaDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        ContractHash = d.ContractHash,
        DefinitionHash = d.DefinitionHash,
        Version = d.Version,
        ChangeKind = d.ChangeKind,
        Fields = d.Fields.Select(CloneSchemaField).ToArray(),
        ValidationRules = d.ValidationRules.Select(CloneSchemaValidationRule).ToArray(),
        References = d.References.ToArray()
    };

    private static SchemaFieldDescriptor CloneSchemaField(SchemaFieldDescriptor f) => new()
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
        CollectionElementType = f.CollectionElementType
    };

    private static SchemaValidationRule CloneSchemaValidationRule(SchemaValidationRule r) => new()
    {
        Name = r.Name,
        Expression = r.Expression,
        ErrorMessage = r.ErrorMessage
    };

    private static FormDescriptor SnapshotForm(FormDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        ContractHash = d.ContractHash,
        DefinitionHash = d.DefinitionHash,
        Version = d.Version,
        Schema = d.Schema,
        Fields = d.Fields.Select(CloneFormField).ToArray(),
        LayoutColumns = d.LayoutColumns
    };

    private static FormFieldDescriptor CloneFormField(FormFieldDescriptor f) => new()
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
        Metadata = f.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal)
    };

    private static CapabilityDescriptor SnapshotCapability(CapabilityDescriptor d) => new()
    {
        Namespace = d.Namespace,
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        Version = d.Version,
        ContractHash = d.ContractHash,
        DefinitionHash = d.DefinitionHash,
        Categories = d.Categories.ToArray(),
        Produces = d.Produces.ToArray(),
        Consumes = d.Consumes.ToArray(),
        SemanticTags = d.SemanticTags.ToArray(),
        CapabilityKind = d.CapabilityKind,
        InputSchema = d.InputSchema,
        OutputSchema = d.OutputSchema,
        Permissions = d.Permissions.ToArray(),
        RiskLevel = d.RiskLevel
    };

    private static HumanTaskDescriptor SnapshotHumanTask(HumanTaskDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        ContractHash = d.ContractHash,
        DefinitionHash = d.DefinitionHash,
        Version = d.Version,
        Interaction = d.Interaction,
        InputSchema = d.InputSchema,
        OutputSchema = d.OutputSchema,
        AssigneeStrategy = d.AssigneeStrategy,
        Timeout = d.Timeout,
        Permissions = d.Permissions,
        Outcomes = d.Outcomes.Select(CloneCompletionOutcome).ToArray()
    };

    private static CompletionOutcome CloneCompletionOutcome(CompletionOutcome o) => new()
    {
        Condition = o.Condition,
        Capability = o.Capability
    };

    private static WorkflowDescriptor SnapshotWorkflow(WorkflowDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        ContractHash = d.ContractHash,
        DefinitionHash = d.DefinitionHash,
        Version = d.Version,
        VariableSchema = d.VariableSchema,
        Steps = d.Steps.Select(CloneWorkflowStep).ToArray(),
        DefaultVariableScope = d.DefaultVariableScope
    };

    private static WorkflowStep CloneWorkflowStep(WorkflowStep s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Target = CloneWorkflowTarget(s.Target),
        Condition = s.Condition,
        Transitions = s.Transitions.ToArray(),
        InputMapping = s.InputMapping,
        OutputMapping = s.OutputMapping,
        OnError = s.OnError
    };

    private static InteractionTarget CloneWorkflowTarget(InteractionTarget target) => target switch
    {
        CapabilityTarget ct => new CapabilityTarget { Capability = ct.Capability },
        HumanTaskTarget ht => new HumanTaskTarget { HumanTask = ht.HumanTask },
        SubWorkflowTarget sw => new SubWorkflowTarget { SubWorkflow = sw.SubWorkflow },
        _ => throw new NotSupportedException($"Unsupported workflow target type: {target.GetType().FullName}")
    };

    private static EventDescriptor SnapshotEvent(EventDescriptor d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        State = d.State,
        SupersededById = d.SupersededById,
        ContractHash = d.ContractHash,
        DefinitionHash = d.DefinitionHash,
        Version = d.Version,
        PayloadSchema = d.PayloadSchema,
        Category = d.Category,
        Semantic = d.Semantic,
        Importance = d.Importance,
        ChangeKind = d.ChangeKind
    };

    private static GeneratedEventDescriptor SnapshotGeneratedEvent(GeneratedEventDescriptor d) => d with
    {
        Producers = d.Producers.ToArray(),
        Consumers = d.Consumers.ToArray()
    };
}
