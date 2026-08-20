using System.Text.Json;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal enum PostgreSqlDescriptorPayloadType
{
    Schema = 1,
    Form = 2,
    Capability = 3,
    HumanTask = 4,
    Workflow = 5,
    Event = 6
}

internal enum PostgreSqlWorkflowTargetType
{
    Capability = 1,
    HumanTask = 2,
    SubWorkflow = 3
}

internal sealed class PostgreSqlDescriptorDraftDocument
{
    public int ContractVersion { get; init; } = 1;
    public string? TenantId { get; init; }
    public string? DraftId { get; init; }
    public DescriptorKind DescriptorKind { get; init; }
    public string? DescriptorId { get; init; }
    public DescriptorDraftOperation Operation { get; init; }
    public DescriptorDraftAuthorKind AuthorKind { get; init; }
    public string? AuthorId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? BaseVersion { get; init; }
    public string? ProposedVersion { get; init; }
    public string? Intent { get; init; }
    public string? Rationale { get; init; }
    public string? CorrelationId { get; init; }
    public string? Source { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
    public DescriptorDraftStatus Status { get; init; }
    public PostgreSqlDescriptorPayloadType PayloadType { get; init; }
    public PostgreSqlSchemaDescriptor? Schema { get; init; }
    public PostgreSqlFormDescriptor? Form { get; init; }
    public PostgreSqlCapabilityDescriptor? Capability { get; init; }
    public PostgreSqlHumanTaskDescriptor? HumanTask { get; init; }
    public PostgreSqlWorkflowDescriptor? Workflow { get; init; }
    public PostgreSqlEventDescriptor? Event { get; init; }
}

internal sealed class PostgreSqlDescriptorReference
{
    public string? Id { get; init; }
    public int Version { get; init; }
    public VersionSelectionMode SelectionMode { get; init; }
    public string? ExpectedContractHash { get; init; }
}

internal sealed class PostgreSqlSchemaDescriptor
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public DescriptorState State { get; init; }
    public string? SupersededById { get; init; }
    public int Version { get; init; }
    public SchemaChangeKind ChangeKind { get; init; }
    public List<PostgreSqlSchemaFieldDescriptor> Fields { get; init; } = new();
    public List<PostgreSqlSchemaValidationRule> ValidationRules { get; init; } = new();
    public List<PostgreSqlDescriptorReference> References { get; init; } = new();
}

internal sealed class PostgreSqlSchemaFieldDescriptor
{
    public string? Name { get; init; }
    public string? FieldType { get; init; }
    public PostgreSqlDescriptorReference? ObjectSchema { get; init; }
    public bool IsRequired { get; init; }
    public bool IsNullable { get; init; }
    public int? MaxLength { get; init; }
    public int? MinLength { get; init; }
    public double? MaxValue { get; init; }
    public double? MinValue { get; init; }
    public string? Pattern { get; init; }
    public bool IsCollection { get; init; }
    public string? CollectionElementType { get; init; }
}

internal sealed class PostgreSqlSchemaValidationRule
{
    public string? Name { get; init; }
    public string? Expression { get; init; }
    public string? ErrorMessage { get; init; }
}

internal sealed class PostgreSqlFormDescriptor
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public DescriptorState State { get; init; }
    public string? SupersededById { get; init; }
    public int Version { get; init; }
    public PostgreSqlDescriptorReference? Schema { get; init; }
    public List<PostgreSqlFormFieldDescriptor> Fields { get; init; } = new();
    public string? LayoutColumns { get; init; }
}

internal sealed class PostgreSqlFormFieldDescriptor
{
    public string? SchemaFieldName { get; init; }
    public string? Label { get; init; }
    public string? Placeholder { get; init; }
    public string? HelpText { get; init; }
    public string? FormatHint { get; init; }
    public int Order { get; init; }
    public string? Group { get; init; }
    public bool IsReadOnly { get; init; }
    public string? VisibilityCondition { get; init; }
    public string? ControlType { get; init; }
    public bool? IsRequiredOverride { get; init; }
    public string? ValidationMessage { get; init; }
    public string? DefaultValueExpression { get; init; }
    public string? OptionsSource { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}

internal sealed class PostgreSqlCapabilityDescriptor
{
    public string? Namespace { get; init; }
    public string? Id { get; init; }
    public string? Name { get; init; }
    public DescriptorState State { get; init; }
    public string? SupersededById { get; init; }
    public int Version { get; init; }
    public List<string> Categories { get; init; } = new();
    public List<PostgreSqlEventReference> Produces { get; init; } = new();
    public List<PostgreSqlEventReference> Consumes { get; init; } = new();
    public List<string> SemanticTags { get; init; } = new();
    public CapabilityKind CapabilityKind { get; init; }
    public PostgreSqlDescriptorReference? InputSchema { get; init; }
    public PostgreSqlDescriptorReference? OutputSchema { get; init; }
    public List<string> Permissions { get; init; } = new();
    public CapabilityRiskLevel RiskLevel { get; init; }
    public CapabilityProjectionKind ProjectionKind { get; init; }
}

internal sealed class PostgreSqlEventReference
{
    public string? Namespace { get; init; }
    public string? Id { get; init; }
    public int? Version { get; init; }
}

internal sealed class PostgreSqlHumanTaskDescriptor
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public DescriptorState State { get; init; }
    public string? SupersededById { get; init; }
    public int Version { get; init; }
    public PostgreSqlDescriptorReference? Interaction { get; init; }
    public PostgreSqlDescriptorReference? InputSchema { get; init; }
    public PostgreSqlDescriptorReference? OutputSchema { get; init; }
    public AssigneeStrategy AssigneeStrategy { get; init; }
    public long? TimeoutTicks { get; init; }
    public string? Permissions { get; init; }
    public List<PostgreSqlCompletionOutcome> Outcomes { get; init; } = new();
}

internal sealed class PostgreSqlCompletionOutcome
{
    public CompletionCondition Condition { get; init; }
    public PostgreSqlDescriptorReference? Capability { get; init; }
}

internal sealed class PostgreSqlWorkflowDescriptor
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public DescriptorState State { get; init; }
    public string? SupersededById { get; init; }
    public int Version { get; init; }
    public PostgreSqlDescriptorReference? VariableSchema { get; init; }
    public List<PostgreSqlWorkflowStep> Steps { get; init; } = new();
    public WorkflowVariableScope DefaultVariableScope { get; init; }
}

internal sealed class PostgreSqlWorkflowStep
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public PostgreSqlWorkflowTarget? Target { get; init; }
    public string? Condition { get; init; }
    public List<string> Transitions { get; init; } = new();
    public string? InputMapping { get; init; }
    public string? OutputMapping { get; init; }
    public StepErrorBehavior OnError { get; init; }
}

internal sealed class PostgreSqlWorkflowTarget
{
    public PostgreSqlWorkflowTargetType Type { get; init; }
    public PostgreSqlDescriptorReference? Capability { get; init; }
    public PostgreSqlDescriptorReference? HumanTask { get; init; }
    public PostgreSqlDescriptorReference? SubWorkflow { get; init; }
}

internal sealed class PostgreSqlEventDescriptor
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public DescriptorState State { get; init; }
    public string? SupersededById { get; init; }
    public int Version { get; init; }
    public PostgreSqlDescriptorReference? PayloadSchema { get; init; }
    public EventCategory Category { get; init; }
    public EventSemantic Semantic { get; init; }
    public EventImportance Importance { get; init; }
    public SchemaChangeKind ChangeKind { get; init; }
}

internal static class PostgreSqlControlPlaneReferenceDataJsonCodec
{
    private const int ContractVersion = 1;

    public static string Serialize(Draft draft)
    {
        var document = ToDocument(draft);
        return System.Text.Json.JsonSerializer.Serialize(
            document,
            PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.PostgreSqlDescriptorDraftDocument);
    }

    public static Draft Deserialize(string json)
    {
        try
        {
            var document = System.Text.Json.JsonSerializer.Deserialize(
                json,
                PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.PostgreSqlDescriptorDraftDocument)
                ?? throw Invariant("Descriptor Draft JSON deserialization returned null.");
            return ToDomain(document);
        }
        catch (JsonException)
        {
            throw Invariant("Descriptor Draft JSON is not a valid persisted contract.");
        }
    }

    private static PostgreSqlDescriptorDraftDocument ToDocument(Draft draft)
    {
        var payload = draft.Payload switch
        {
            SchemaDescriptorDraftPayload value => new PayloadDocument(
                PostgreSqlDescriptorPayloadType.Schema, ToSchema(value.Descriptor), null, null, null, null, null),
            FormDescriptorDraftPayload value => new PayloadDocument(
                PostgreSqlDescriptorPayloadType.Form, null, ToForm(value.Descriptor), null, null, null, null),
            CapabilityDescriptorDraftPayload value => new PayloadDocument(
                PostgreSqlDescriptorPayloadType.Capability, null, null, ToCapability(value.Descriptor), null, null, null),
            HumanTaskDescriptorDraftPayload value => new PayloadDocument(
                PostgreSqlDescriptorPayloadType.HumanTask, null, null, null, ToHumanTask(value.Descriptor), null, null),
            WorkflowDescriptorDraftPayload value => new PayloadDocument(
                PostgreSqlDescriptorPayloadType.Workflow, null, null, null, null, ToWorkflow(value.Descriptor), null),
            EventDescriptorDraftPayload value => new PayloadDocument(
                PostgreSqlDescriptorPayloadType.Event, null, null, null, null, null, ToEvent(value.Descriptor)),
            _ => throw Invariant($"Unsupported Descriptor Draft payload type {draft.Payload.GetType().FullName}.")
        };

        return new PostgreSqlDescriptorDraftDocument
        {
            ContractVersion = ContractVersion,
            TenantId = draft.TenantId,
            DraftId = draft.DraftId,
            DescriptorKind = draft.DescriptorKind,
            DescriptorId = draft.DescriptorId,
            Operation = draft.Operation,
            AuthorKind = draft.AuthorKind,
            AuthorId = draft.AuthorId,
            CreatedAt = draft.CreatedAt,
            BaseVersion = draft.BaseVersion,
            ProposedVersion = draft.ProposedVersion,
            Intent = draft.Intent,
            Rationale = draft.Rationale,
            CorrelationId = draft.CorrelationId,
            Source = draft.Source,
            Metadata = draft.Metadata is null
                ? null
                : new Dictionary<string, string>(draft.Metadata, StringComparer.Ordinal),
            Status = draft.Status,
            PayloadType = payload.Type,
            Schema = payload.Schema,
            Form = payload.Form,
            Capability = payload.Capability,
            HumanTask = payload.HumanTask,
            Workflow = payload.Workflow,
            Event = payload.Event
        };
    }

    private static Draft ToDomain(PostgreSqlDescriptorDraftDocument document)
    {
        if (document.ContractVersion != ContractVersion)
            throw Invariant("Descriptor Draft JSON contract version is unsupported.");
        Require(document.TenantId, "tenantId");
        Require(document.DraftId, "draftId");
        var populatedArmCount = (document.Schema is null ? 0 : 1)
            + (document.Form is null ? 0 : 1)
            + (document.Capability is null ? 0 : 1)
            + (document.HumanTask is null ? 0 : 1)
            + (document.Workflow is null ? 0 : 1)
            + (document.Event is null ? 0 : 1);
        if (populatedArmCount != 1)
            throw Invariant("Descriptor Draft JSON must contain exactly one payload arm.");

        DescriptorDraftPayload payload = document.PayloadType switch
        {
            PostgreSqlDescriptorPayloadType.Schema => new SchemaDescriptorDraftPayload(ToSchema(document.Schema)),
            PostgreSqlDescriptorPayloadType.Form => new FormDescriptorDraftPayload(ToForm(document.Form)),
            PostgreSqlDescriptorPayloadType.Capability => new CapabilityDescriptorDraftPayload(ToCapability(document.Capability)),
            PostgreSqlDescriptorPayloadType.HumanTask => new HumanTaskDescriptorDraftPayload(ToHumanTask(document.HumanTask)),
            PostgreSqlDescriptorPayloadType.Workflow => new WorkflowDescriptorDraftPayload(ToWorkflow(document.Workflow)),
            PostgreSqlDescriptorPayloadType.Event => new EventDescriptorDraftPayload(ToEvent(document.Event)),
            _ => throw Invariant("Descriptor Draft JSON contains an unsupported payload type.")
        };

        return new Draft
        {
            TenantId = document.TenantId!,
            DraftId = document.DraftId!,
            DescriptorKind = document.DescriptorKind,
            DescriptorId = document.DescriptorId!,
            Operation = document.Operation,
            AuthorKind = document.AuthorKind,
            AuthorId = document.AuthorId!,
            CreatedAt = document.CreatedAt,
            BaseVersion = document.BaseVersion,
            ProposedVersion = document.ProposedVersion,
            Intent = document.Intent,
            Rationale = document.Rationale,
            CorrelationId = document.CorrelationId,
            Source = document.Source,
            Metadata = document.Metadata is null
                ? null
                : new Dictionary<string, string>(document.Metadata, StringComparer.Ordinal),
            Status = document.Status,
            Payload = payload
        };
    }

    private static PostgreSqlSchemaDescriptor ToSchema(SchemaDescriptor descriptor) => new()
    {
        Id = descriptor.Id,
        Name = descriptor.Name,
        State = descriptor.State,
        SupersededById = descriptor.SupersededById,
        Version = descriptor.Version,
        ChangeKind = descriptor.ChangeKind,
        Fields = descriptor.Fields.Select(field => new PostgreSqlSchemaFieldDescriptor
        {
            Name = field.Name,
            FieldType = field.FieldType,
            ObjectSchema = ToReference(field.ObjectSchema),
            IsRequired = field.IsRequired,
            IsNullable = field.IsNullable,
            MaxLength = field.MaxLength,
            MinLength = field.MinLength,
            MaxValue = field.MaxValue,
            MinValue = field.MinValue,
            Pattern = field.Pattern,
            IsCollection = field.IsCollection,
            CollectionElementType = field.CollectionElementType
        }).ToList(),
        ValidationRules = descriptor.ValidationRules.Select(rule => new PostgreSqlSchemaValidationRule
        {
            Name = rule.Name,
            Expression = rule.Expression,
            ErrorMessage = rule.ErrorMessage
        }).ToList(),
        References = descriptor.References.Select(ToReference).ToList()
    };

    private static SchemaDescriptor ToSchema(PostgreSqlSchemaDescriptor? descriptor)
    {
        var value = Require(descriptor, "schema payload");
        return new SchemaDescriptor
        {
            Id = Require(value.Id, "schema.id"),
            Name = Require(value.Name, "schema.name"),
            State = value.State,
            SupersededById = value.SupersededById,
            Version = value.Version,
            ChangeKind = value.ChangeKind,
            Fields = Require(value.Fields, "schema.fields").Select(field => new SchemaFieldDescriptor
            {
                Name = Require(field.Name, "schema.fields[].name"),
                FieldType = Require(field.FieldType, "schema.fields[].fieldType"),
                ObjectSchema = ToNullableSchemaReference(field.ObjectSchema),
                IsRequired = field.IsRequired,
                IsNullable = field.IsNullable,
                MaxLength = field.MaxLength,
                MinLength = field.MinLength,
                MaxValue = field.MaxValue,
                MinValue = field.MinValue,
                Pattern = field.Pattern,
                IsCollection = field.IsCollection,
                CollectionElementType = field.CollectionElementType
            }).ToArray(),
            ValidationRules = Require(value.ValidationRules, "schema.validationRules").Select(rule => new SchemaValidationRule
            {
                Name = Require(rule.Name, "schema.validationRules[].name"),
                Expression = Require(rule.Expression, "schema.validationRules[].expression"),
                ErrorMessage = rule.ErrorMessage
            }).ToArray(),
            References = Require(value.References, "schema.references").Select(ToSchemaReference).ToArray()
        };
    }

    private static PostgreSqlFormDescriptor ToForm(FormDescriptor descriptor) => new()
    {
        Id = descriptor.Id,
        Name = descriptor.Name,
        State = descriptor.State,
        SupersededById = descriptor.SupersededById,
        Version = descriptor.Version,
        Schema = ToReference(descriptor.Schema),
        Fields = descriptor.Fields.Select(field => new PostgreSqlFormFieldDescriptor
        {
            SchemaFieldName = field.SchemaFieldName,
            Label = field.Label,
            Placeholder = field.Placeholder,
            HelpText = field.HelpText,
            FormatHint = field.FormatHint,
            Order = field.Order,
            Group = field.Group,
            IsReadOnly = field.IsReadOnly,
            VisibilityCondition = field.VisibilityCondition,
            ControlType = field.ControlType,
            IsRequiredOverride = field.IsRequiredOverride,
            ValidationMessage = field.ValidationMessage,
            DefaultValueExpression = field.DefaultValueExpression,
            OptionsSource = field.OptionsSource,
            Metadata = new Dictionary<string, string>(field.Metadata, StringComparer.Ordinal)
        }).ToList(),
        LayoutColumns = descriptor.LayoutColumns
    };

    private static FormDescriptor ToForm(PostgreSqlFormDescriptor? descriptor)
    {
        var value = Require(descriptor, "form payload");
        return new FormDescriptor
        {
            Id = Require(value.Id, "form.id"),
            Name = Require(value.Name, "form.name"),
            State = value.State,
            SupersededById = value.SupersededById,
            Version = value.Version,
            Schema = ToSchemaReference(value.Schema),
            Fields = Require(value.Fields, "form.fields").Select(field => new FormFieldDescriptor
            {
                SchemaFieldName = Require(field.SchemaFieldName, "form.fields[].schemaFieldName"),
                Label = field.Label,
                Placeholder = field.Placeholder,
                HelpText = field.HelpText,
                FormatHint = field.FormatHint,
                Order = field.Order,
                Group = field.Group,
                IsReadOnly = field.IsReadOnly,
                VisibilityCondition = field.VisibilityCondition,
                ControlType = field.ControlType,
                IsRequiredOverride = field.IsRequiredOverride,
                ValidationMessage = field.ValidationMessage,
                DefaultValueExpression = field.DefaultValueExpression,
                OptionsSource = field.OptionsSource,
                Metadata = new Dictionary<string, string>(field.Metadata, StringComparer.Ordinal)
            }).ToArray(),
            LayoutColumns = value.LayoutColumns
        };
    }

    private static PostgreSqlCapabilityDescriptor ToCapability(CapabilityDescriptor descriptor) => new()
    {
        Namespace = descriptor.Namespace,
        Id = descriptor.Id,
        Name = descriptor.Name,
        State = descriptor.State,
        SupersededById = descriptor.SupersededById,
        Version = descriptor.Version,
        Categories = descriptor.Categories.ToList(),
        Produces = descriptor.Produces.Select(value => new PostgreSqlEventReference
        {
            Namespace = value.Namespace,
            Id = value.Id,
            Version = value.Version
        }).ToList(),
        Consumes = descriptor.Consumes.Select(value => new PostgreSqlEventReference
        {
            Namespace = value.Namespace,
            Id = value.Id,
            Version = value.Version
        }).ToList(),
        SemanticTags = descriptor.SemanticTags.ToList(),
        CapabilityKind = descriptor.CapabilityKind,
        InputSchema = ToReference(descriptor.InputSchema),
        OutputSchema = ToReference(descriptor.OutputSchema),
        Permissions = descriptor.Permissions.ToList(),
        RiskLevel = descriptor.RiskLevel,
        ProjectionKind = descriptor.ProjectionKind
    };

    private static CapabilityDescriptor ToCapability(PostgreSqlCapabilityDescriptor? descriptor)
    {
        var value = Require(descriptor, "capability payload");
        return new CapabilityDescriptor
        {
            Namespace = Require(value.Namespace, "capability.namespace"),
            Id = Require(value.Id, "capability.id"),
            Name = Require(value.Name, "capability.name"),
            State = value.State,
            SupersededById = value.SupersededById,
            Version = value.Version,
            Categories = value.Categories.ToArray(),
            Produces = Require(value.Produces, "capability.produces").Select(eventRef => new EventRef
            {
                Namespace = Require(eventRef.Namespace, "capability.produces[].namespace"),
                Id = Require(eventRef.Id, "capability.produces[].id"),
                Version = eventRef.Version
            }).ToArray(),
            Consumes = Require(value.Consumes, "capability.consumes").Select(eventRef => new EventRef
            {
                Namespace = Require(eventRef.Namespace, "capability.consumes[].namespace"),
                Id = Require(eventRef.Id, "capability.consumes[].id"),
                Version = eventRef.Version
            }).ToArray(),
            SemanticTags = value.SemanticTags.ToArray(),
            CapabilityKind = value.CapabilityKind,
            InputSchema = ToNullableSchemaReference(value.InputSchema),
            OutputSchema = ToNullableSchemaReference(value.OutputSchema),
            Permissions = value.Permissions.ToArray(),
            RiskLevel = value.RiskLevel,
            ProjectionKind = value.ProjectionKind
        };
    }

    private static PostgreSqlHumanTaskDescriptor ToHumanTask(HumanTaskDescriptor descriptor) => new()
    {
        Id = descriptor.Id,
        Name = descriptor.Name,
        State = descriptor.State,
        SupersededById = descriptor.SupersededById,
        Version = descriptor.Version,
        Interaction = ToReference(descriptor.Interaction),
        InputSchema = ToReference(descriptor.InputSchema),
        OutputSchema = ToReference(descriptor.OutputSchema),
        AssigneeStrategy = descriptor.AssigneeStrategy,
        TimeoutTicks = descriptor.Timeout?.Ticks,
        Permissions = descriptor.Permissions,
        Outcomes = descriptor.Outcomes.Select(outcome => new PostgreSqlCompletionOutcome
        {
            Condition = outcome.Condition,
            Capability = ToReference(outcome.Capability)
        }).ToList()
    };

    private static HumanTaskDescriptor ToHumanTask(PostgreSqlHumanTaskDescriptor? descriptor)
    {
        var value = Require(descriptor, "human task payload");
        return new HumanTaskDescriptor
        {
            Id = Require(value.Id, "humanTask.id"),
            Name = Require(value.Name, "humanTask.name"),
            State = value.State,
            SupersededById = value.SupersededById,
            Version = value.Version,
            Interaction = ToInteractionReference(value.Interaction),
            InputSchema = ToNullableSchemaReference(value.InputSchema),
            OutputSchema = ToNullableSchemaReference(value.OutputSchema),
            AssigneeStrategy = value.AssigneeStrategy,
            Timeout = value.TimeoutTicks is null ? null : TimeSpan.FromTicks(value.TimeoutTicks.Value),
            Permissions = value.Permissions,
            Outcomes = Require(value.Outcomes, "humanTask.outcomes").Select(outcome => new CompletionOutcome
            {
                Condition = outcome.Condition,
                Capability = ToNullableVersionedReference(outcome.Capability)
            }).ToArray()
        };
    }

    private static PostgreSqlWorkflowDescriptor ToWorkflow(WorkflowDescriptor descriptor) => new()
    {
        Id = descriptor.Id,
        Name = descriptor.Name,
        State = descriptor.State,
        SupersededById = descriptor.SupersededById,
        Version = descriptor.Version,
        VariableSchema = ToReference(descriptor.VariableSchema),
        Steps = descriptor.Steps.Select(step => new PostgreSqlWorkflowStep
        {
            Id = step.Id,
            Name = step.Name,
            Target = ToWorkflowTarget(step.Target),
            Condition = step.Condition,
            Transitions = step.Transitions.ToList(),
            InputMapping = step.InputMapping,
            OutputMapping = step.OutputMapping,
            OnError = step.OnError
        }).ToList(),
        DefaultVariableScope = descriptor.DefaultVariableScope
    };

    private static WorkflowDescriptor ToWorkflow(PostgreSqlWorkflowDescriptor? descriptor)
    {
        var value = Require(descriptor, "workflow payload");
        return new WorkflowDescriptor
        {
            Id = Require(value.Id, "workflow.id"),
            Name = Require(value.Name, "workflow.name"),
            State = value.State,
            SupersededById = value.SupersededById,
            Version = value.Version,
            VariableSchema = ToNullableSchemaReference(value.VariableSchema),
            Steps = Require(value.Steps, "workflow.steps").Select(step => new WorkflowStep
            {
                Id = Require(step.Id, "workflow.steps[].id"),
                Name = Require(step.Name, "workflow.steps[].name"),
                Target = ToWorkflowTarget(step.Target),
                Condition = step.Condition,
                Transitions = step.Transitions.ToArray(),
                InputMapping = step.InputMapping,
                OutputMapping = step.OutputMapping,
                OnError = step.OnError
            }).ToArray(),
            DefaultVariableScope = value.DefaultVariableScope
        };
    }

    private static PostgreSqlWorkflowTarget ToWorkflowTarget(InteractionTarget target)
    {
        if (target is null)
            throw Invariant("Workflow target is null.");

        return target switch
        {
            CapabilityTarget value => new PostgreSqlWorkflowTarget
            {
                Type = PostgreSqlWorkflowTargetType.Capability,
                Capability = ToReference(value.Capability)
            },
            HumanTaskTarget value => new PostgreSqlWorkflowTarget
            {
                Type = PostgreSqlWorkflowTargetType.HumanTask,
                HumanTask = ToReference(value.HumanTask)
            },
            SubWorkflowTarget value => new PostgreSqlWorkflowTarget
            {
                Type = PostgreSqlWorkflowTargetType.SubWorkflow,
                SubWorkflow = ToReference(value.SubWorkflow)
            },
            _ => throw Invariant($"Unsupported Workflow target type {target.GetType().FullName}.")
        };
    }

    private static InteractionTarget ToWorkflowTarget(PostgreSqlWorkflowTarget? target)
    {
        var value = Require(target, "workflow target");
        var populatedArmCount = (value.Capability is null ? 0 : 1)
            + (value.HumanTask is null ? 0 : 1)
            + (value.SubWorkflow is null ? 0 : 1);
        if (populatedArmCount != 1)
            throw Invariant("workflow.target must contain exactly one reference arm.");

        return value.Type switch
        {
            PostgreSqlWorkflowTargetType.Capability => new CapabilityTarget
            {
                Capability = ToReference<IVersionedDescriptor>(value.Capability, "workflow.target.capability")
            },
            PostgreSqlWorkflowTargetType.HumanTask => new HumanTaskTarget
            {
                HumanTask = ToReference<HumanTaskDescriptor>(value.HumanTask, "workflow.target.humanTask")
            },
            PostgreSqlWorkflowTargetType.SubWorkflow => new SubWorkflowTarget
            {
                SubWorkflow = ToReference<WorkflowDescriptor>(value.SubWorkflow, "workflow.target.subWorkflow")
            },
            _ => throw Invariant("Workflow target type is unsupported.")
        };
    }

    private static PostgreSqlEventDescriptor ToEvent(EventDescriptor descriptor) => new()
    {
        Id = descriptor.Id,
        Name = descriptor.Name,
        State = descriptor.State,
        SupersededById = descriptor.SupersededById,
        Version = descriptor.Version,
        PayloadSchema = ToReference(descriptor.PayloadSchema),
        Category = descriptor.Category,
        Semantic = descriptor.Semantic,
        Importance = descriptor.Importance,
        ChangeKind = descriptor.ChangeKind
    };

    private static EventDescriptor ToEvent(PostgreSqlEventDescriptor? descriptor)
    {
        var value = Require(descriptor, "event payload");
        return new EventDescriptor
        {
            Id = Require(value.Id, "event.id"),
            Name = Require(value.Name, "event.name"),
            State = value.State,
            SupersededById = value.SupersededById,
            Version = value.Version,
            PayloadSchema = ToSchemaReference(value.PayloadSchema),
            Category = value.Category,
            Semantic = value.Semantic,
            Importance = value.Importance,
            ChangeKind = value.ChangeKind
        };
    }

    private static PostgreSqlDescriptorReference? ToReference<T>(VersionedDescriptorRef<T>? value)
        where T : IVersionedDescriptor
        => value is null ? null : ToReference(value.Value);

    private static PostgreSqlDescriptorReference ToReference<T>(VersionedDescriptorRef<T> value)
        where T : IVersionedDescriptor
        => new()
        {
            Id = value.Id,
            Version = value.Version,
            SelectionMode = value.SelectionMode,
            ExpectedContractHash = value.ExpectedContractHash
        };

    private static VersionedDescriptorRef<T> ToReference<T>(PostgreSqlDescriptorReference? value, string path)
        where T : IVersionedDescriptor
    {
        var reference = Require(value, path);
        return new VersionedDescriptorRef<T>(
            Require(reference.Id, $"{path}.id"),
            reference.Version,
            reference.SelectionMode,
            reference.ExpectedContractHash);
    }

    private static VersionedDescriptorRef<SchemaDescriptor> ToSchemaReference(PostgreSqlDescriptorReference? value)
        => ToReference<SchemaDescriptor>(value, "schema reference");

    private static VersionedDescriptorRef<SchemaDescriptor>? ToNullableSchemaReference(PostgreSqlDescriptorReference? value)
        => value is null ? null : ToReference<SchemaDescriptor>(value, "schema reference");

    private static VersionedDescriptorRef<IInteractionDescriptor> ToInteractionReference(PostgreSqlDescriptorReference? value)
        => ToReference<IInteractionDescriptor>(value, "interaction reference");

    private static VersionedDescriptorRef<IInteractionDescriptor>? ToNullableInteractionReference(PostgreSqlDescriptorReference? value)
        => value is null ? null : ToReference<IInteractionDescriptor>(value, "interaction reference");

    private static VersionedDescriptorRef<IVersionedDescriptor>? ToNullableVersionedReference(PostgreSqlDescriptorReference? value)
        => value is null ? null : ToReference<IVersionedDescriptor>(value, "descriptor reference");

    private static T Require<T>(T? value, string path) where T : class
        => value ?? throw Invariant($"Persisted Descriptor Draft member '{path}' is null.");

    private static string Require(string? value, string path)
        => value ?? throw Invariant($"Persisted Descriptor Draft member '{path}' is null.");

    private static RuntimePersistenceContractException Invariant(string message)
        => PostgreSqlControlPlaneReferenceDataStoreSupport.PersistedInvariant(message);

    private readonly record struct PayloadDocument(
        PostgreSqlDescriptorPayloadType Type,
        PostgreSqlSchemaDescriptor? Schema,
        PostgreSqlFormDescriptor? Form,
        PostgreSqlCapabilityDescriptor? Capability,
        PostgreSqlHumanTaskDescriptor? HumanTask,
        PostgreSqlWorkflowDescriptor? Workflow,
        PostgreSqlEventDescriptor? Event);
}
