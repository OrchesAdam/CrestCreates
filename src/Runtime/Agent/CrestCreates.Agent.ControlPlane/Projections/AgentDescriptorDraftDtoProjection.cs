using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.Event.Abstractions;
using CrestCreates.Schema.Abstractions;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Projections;

/// <summary>
/// Bidirectional projection between DescriptorDraft and AgentDescriptorDraftDto.
/// FromDraft: domain → DTO (for results).
/// ToDomainPayload: DTO → domain (for requests).
/// Lives in ControlPlane (not Abstractions) because it depends on domain types.
/// </summary>
internal static class AgentDescriptorDraftDtoProjection
{
    public static AgentDescriptorDraftDto FromDraft(DraftAbstractions.DescriptorDraft draft)
    {
        var descriptor = draft.Payload.GetDescriptor();

        return new AgentDescriptorDraftDto
        {
            TenantId = draft.TenantId,
            DraftId = draft.DraftId,
            DescriptorKind = draft.DescriptorKind,
            DescriptorId = draft.DescriptorId,
            Operation = draft.Operation,
            AuthorKind = draft.AuthorKind,
            AuthorId = draft.AuthorId,
            CreatedAt = draft.CreatedAt,
            Payload = MapPayload(draft.DescriptorKind, descriptor),
            BaseVersion = draft.BaseVersion,
            ProposedVersion = draft.ProposedVersion,
            Intent = draft.Intent,
            Rationale = draft.Rationale,
            CorrelationId = draft.CorrelationId,
            Source = draft.Source,
            Metadata = draft.Metadata,
            Status = draft.Status,
        };
    }

    private static AgentDraftPayloadDto MapPayload(
        DescriptorKind kind,
        IDescriptor descriptor)
    {
        return kind switch
        {
            DescriptorKind.Capability => new AgentDraftPayloadDto
            {
                Discriminator = DescriptorKind.Capability,
                Capability = MapCapability(descriptor),
            },
            DescriptorKind.Workflow => new AgentDraftPayloadDto
            {
                Discriminator = DescriptorKind.Workflow,
                Workflow = MapWorkflow(descriptor),
            },
            DescriptorKind.HumanTask => new AgentDraftPayloadDto
            {
                Discriminator = DescriptorKind.HumanTask,
                HumanTask = MapHumanTask(descriptor),
            },
            DescriptorKind.Form => new AgentDraftPayloadDto
            {
                Discriminator = DescriptorKind.Form,
                Form = MapForm(descriptor),
            },
            DescriptorKind.Event => new AgentDraftPayloadDto
            {
                Discriminator = DescriptorKind.Event,
                Event = MapEvent(descriptor),
            },
            DescriptorKind.Schema => new AgentDraftPayloadDto
            {
                Discriminator = DescriptorKind.Schema,
                Schema = MapSchema(descriptor),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported descriptor kind."),
        };
    }

    // ── FromDraft sub-mappers (use IDescriptor for robustness) ─────

    private static AgentCapabilityDraftPayloadDto MapCapability(IDescriptor d)
    {
        var cap = d as CapabilityDescriptor;
        return new AgentCapabilityDraftPayloadDto
        {
            DescriptorRef = new DescriptorRef(d.Namespace, d.Id),
            Name = d.Name,
            DisplayName = d.Name,
            State = d.State.ToString(),
            InputSchema = cap?.InputSchema is { } ischema ? new DescriptorRef("schema", ischema.Id, ischema.Version) : null,
            OutputSchema = cap?.OutputSchema is { } oschema ? new DescriptorRef("schema", oschema.Id, oschema.Version) : null,
            CapabilityKind = cap?.CapabilityKind.ToString(),
            Categories = cap?.Categories.ToArray() ?? [],
            Produces = cap?.Produces.Select(e => new DescriptorRef(e.Namespace, e.Id, e.Version)).ToArray() ?? [],
            Consumes = cap?.Consumes.Select(e => new DescriptorRef(e.Namespace, e.Id, e.Version)).ToArray() ?? [],
            SemanticTags = cap?.SemanticTags.ToArray() ?? [],
            Permissions = cap?.Permissions.ToArray() ?? [],
            RiskLevel = cap?.RiskLevel.ToString(),
            ContractHash = d.ContractHash,
            DefinitionHash = d.DefinitionHash,
            Version = d is IVersionedDescriptor vd && vd.Version > 0 ? vd.Version : null,
        };
    }

    private static AgentWorkflowDraftPayloadDto MapWorkflow(IDescriptor d)
    {
        var wf = d as Workflow.Abstractions.WorkflowDescriptor;
        return new AgentWorkflowDraftPayloadDto
        {
            DescriptorRef = new DescriptorRef(d.Namespace, d.Id),
            Name = d.Name,
            DisplayName = d.Name,
            State = d.State.ToString(),
            ContractHash = d.ContractHash,
            DefinitionHash = d.DefinitionHash,
            Version = d is IVersionedDescriptor vd && vd.Version > 0 ? vd.Version : null,
            VariableSchema = wf?.VariableSchema is { } vschema ? new DescriptorRef("schema", vschema.Id, vschema.Version) : null,
        };
    }

    private static AgentHumanTaskDraftPayloadDto MapHumanTask(IDescriptor d)
    {
        var ht = d as HumanTask.Abstractions.HumanTaskDescriptor;
        return new AgentHumanTaskDraftPayloadDto
        {
            DescriptorRef = new DescriptorRef(d.Namespace, d.Id),
            Name = d.Name,
            DisplayName = d.Name,
            State = d.State.ToString(),
            AssignmentStrategy = ht?.AssigneeStrategy.ToString(),
            ContractHash = d.ContractHash,
            DefinitionHash = d.DefinitionHash,
            Version = d is IVersionedDescriptor vd && vd.Version > 0 ? vd.Version : null,
            InputSchema = ht?.InputSchema is { } ischema ? new DescriptorRef("schema", ischema.Id, ischema.Version) : null,
            OutputSchema = ht?.OutputSchema is { } oschema ? new DescriptorRef("schema", oschema.Id, oschema.Version) : null,
            Interaction = ht?.Interaction is { } interaction ? new DescriptorRef("form", interaction.Id, interaction.Version) : null,
            Timeout = ht?.Timeout?.ToString(),
        };
    }

    private static AgentFormDraftPayloadDto MapForm(IDescriptor d)
    {
        var form = d as Form.Abstractions.FormDescriptor;
        return new AgentFormDraftPayloadDto
        {
            DescriptorRef = new DescriptorRef(d.Namespace, d.Id),
            Name = d.Name,
            DisplayName = d.Name,
            State = d.State.ToString(),
            FormSchema = form?.Schema is { } fschema ? new DescriptorRef("schema", fschema.Id, fschema.Version) : null,
            ContractHash = d.ContractHash,
            DefinitionHash = d.DefinitionHash,
            Version = d is IVersionedDescriptor vd && vd.Version > 0 ? vd.Version : null,
        };
    }

    private static AgentEventDraftPayloadDto MapEvent(IDescriptor d)
    {
        var evt = d as Event.Abstractions.EventDescriptor;
        return new AgentEventDraftPayloadDto
        {
            DescriptorRef = new DescriptorRef(d.Namespace, d.Id),
            Name = d.Name,
            DisplayName = d.Name,
            State = d.State.ToString(),
            EventKind = evt?.Category.ToString(),
            EventType = evt?.Semantic.ToString(),
            ContractHash = d.ContractHash,
            DefinitionHash = d.DefinitionHash,
            Version = d is IVersionedDescriptor vd && vd.Version > 0 ? vd.Version : null,
            PayloadSchema = evt?.PayloadSchema is { } pschema ? new DescriptorRef("schema", pschema.Id, pschema.Version) : null,
            Importance = evt?.Importance.ToString(),
            ChangeKind = evt?.ChangeKind.ToString(),
        };
    }

    private static AgentSchemaDraftPayloadDto MapSchema(IDescriptor d)
    {
        var schema = d as Schema.Abstractions.SchemaDescriptor;
        return new AgentSchemaDraftPayloadDto
        {
            DescriptorRef = new DescriptorRef(d.Namespace, d.Id),
            Name = d.Name,
            DisplayName = d.Name,
            State = d.State.ToString(),
            SchemaKind = schema?.ChangeKind.ToString(),
            ContractHash = d.ContractHash,
            DefinitionHash = d.DefinitionHash,
            Version = d is IVersionedDescriptor vd && vd.Version > 0 ? vd.Version : null,
        };
    }

    // ── ToDomainPayload ────────────────────────────────────────────

    public static DraftAbstractions.DescriptorDraftPayload ToDomainPayload(AgentDraftPayloadDto dto)
    {
        ValidateDiscriminator(dto);

        return dto.Discriminator switch
        {
            DescriptorKind.Capability => MapToCapabilityPayload(dto.Capability!),
            DescriptorKind.Workflow => MapToWorkflowPayload(dto.Workflow!),
            DescriptorKind.HumanTask => MapToHumanTaskPayload(dto.HumanTask!),
            DescriptorKind.Form => MapToFormPayload(dto.Form!),
            DescriptorKind.Event => MapToEventPayload(dto.Event!),
            DescriptorKind.Schema => MapToSchemaPayload(dto.Schema!),
            _ => throw new ArgumentOutOfRangeException(
                nameof(dto.Discriminator), dto.Discriminator, "Unsupported descriptor kind."),
        };
    }

    private static void ValidateDiscriminator(AgentDraftPayloadDto dto)
    {
        var kind = dto.Discriminator;
        bool hasCap = dto.Capability is not null;
        bool hasWorkflow = dto.Workflow is not null;
        bool hasHumanTask = dto.HumanTask is not null;
        bool hasForm = dto.Form is not null;
        bool hasEvent = dto.Event is not null;
        bool hasSchema = dto.Schema is not null;

        bool match = kind switch
        {
            DescriptorKind.Capability => hasCap && !hasWorkflow && !hasHumanTask && !hasForm && !hasEvent && !hasSchema,
            DescriptorKind.Workflow => hasWorkflow && !hasCap && !hasHumanTask && !hasForm && !hasEvent && !hasSchema,
            DescriptorKind.HumanTask => hasHumanTask && !hasCap && !hasWorkflow && !hasForm && !hasEvent && !hasSchema,
            DescriptorKind.Form => hasForm && !hasCap && !hasWorkflow && !hasHumanTask && !hasEvent && !hasSchema,
            DescriptorKind.Event => hasEvent && !hasCap && !hasWorkflow && !hasHumanTask && !hasForm && !hasSchema,
            DescriptorKind.Schema => hasSchema && !hasCap && !hasWorkflow && !hasHumanTask && !hasForm && !hasEvent,
            _ => false,
        };

        if (!match)
            throw new InvalidOperationException(
                $"Discriminator {kind} does not match the populated sub-record. " +
                "Only the sub-record matching Discriminator may be non-null.");
    }

    private static DraftAbstractions.DescriptorDraftPayload MapToCapabilityPayload(
        AgentCapabilityDraftPayloadDto dto)
    {
        return new DraftAbstractions.CapabilityDescriptorDraftPayload(
            new CapabilityDescriptor
            {
                Id = dto.DescriptorRef?.Id ?? dto.Name ?? "",
                Name = dto.Name ?? "",
                State = Enum.TryParse<DescriptorState>(dto.State, out var state)
                    ? state
                    : DescriptorState.Active,
                ContractHash = dto.ContractHash ?? "",
                DefinitionHash = dto.DefinitionHash ?? "",
                Version = dto.Version ?? 0,
                InputSchema = dto.InputSchema is { } ischema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(ischema.Id, ischema.Version ?? 1)
                    : null,
                OutputSchema = dto.OutputSchema is { } oschema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(oschema.Id, oschema.Version ?? 1)
                    : null,
                CapabilityKind = Enum.TryParse<Metadata.Abstractions.CapabilityKind>(
                    dto.CapabilityKind, out var capKind)
                    ? capKind
                    : default,
                Categories = dto.Categories ?? Array.Empty<string>(),
                Produces = (dto.Produces ?? Array.Empty<DescriptorRef>())
                    .Select(r => new EventRef(r.Namespace, r.Id, r.Version))
                    .ToArray(),
                Consumes = (dto.Consumes ?? Array.Empty<DescriptorRef>())
                    .Select(r => new EventRef(r.Namespace, r.Id, r.Version))
                    .ToArray(),
                SemanticTags = dto.SemanticTags ?? Array.Empty<string>(),
                Permissions = dto.Permissions ?? Array.Empty<string>(),
                RiskLevel = Enum.TryParse<Metadata.Abstractions.CapabilityRiskLevel>(
                    dto.RiskLevel, out var riskLevel)
                    ? riskLevel
                    : Metadata.Abstractions.CapabilityRiskLevel.Medium,
            });
    }

    private static DraftAbstractions.DescriptorDraftPayload MapToWorkflowPayload(
        AgentWorkflowDraftPayloadDto dto)
    {
        return new DraftAbstractions.WorkflowDescriptorDraftPayload(
            new Workflow.Abstractions.WorkflowDescriptor
            {
                Id = dto.DescriptorRef?.Id ?? dto.Name ?? "",
                Name = dto.Name ?? "",
                State = Enum.TryParse<DescriptorState>(dto.State, out var state)
                    ? state
                    : DescriptorState.Active,
                ContractHash = dto.ContractHash ?? "",
                DefinitionHash = dto.DefinitionHash ?? "",
                Version = dto.Version ?? 0,
                VariableSchema = dto.VariableSchema is { } vschema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(vschema.Id, vschema.Version ?? 1)
                    : null,
            });
    }

    private static DraftAbstractions.DescriptorDraftPayload MapToHumanTaskPayload(
        AgentHumanTaskDraftPayloadDto dto)
    {
        return new DraftAbstractions.HumanTaskDescriptorDraftPayload(
            new HumanTask.Abstractions.HumanTaskDescriptor
            {
                Id = dto.DescriptorRef?.Id ?? dto.Name ?? "",
                Name = dto.Name ?? "",
                State = Enum.TryParse<DescriptorState>(dto.State, out var state)
                    ? state
                    : DescriptorState.Active,
                ContractHash = dto.ContractHash ?? "",
                DefinitionHash = dto.DefinitionHash ?? "",
                Version = dto.Version ?? 0,
                AssigneeStrategy = Enum.TryParse<HumanTask.Abstractions.AssigneeStrategy>(
                    dto.AssignmentStrategy, out var strategy)
                    ? strategy
                    : default,
                InputSchema = dto.InputSchema is { } ischema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(ischema.Id, ischema.Version ?? 1)
                    : null,
                OutputSchema = dto.OutputSchema is { } oschema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(oschema.Id, oschema.Version ?? 1)
                    : null,
                Interaction = dto.Interaction is { } interaction
                    ? new VersionedDescriptorRef<IInteractionDescriptor>(interaction.Id, interaction.Version ?? 1)
                    : default,
                Timeout = dto.Timeout is not null && TimeSpan.TryParse(dto.Timeout, out var timeout)
                    ? timeout
                    : null,
            });
    }

    private static DraftAbstractions.DescriptorDraftPayload MapToFormPayload(
        AgentFormDraftPayloadDto dto)
    {
        return new DraftAbstractions.FormDescriptorDraftPayload(
            new Form.Abstractions.FormDescriptor
            {
                Id = dto.DescriptorRef?.Id ?? dto.Name ?? "",
                Name = dto.Name ?? "",
                State = Enum.TryParse<DescriptorState>(dto.State, out var state)
                    ? state
                    : DescriptorState.Active,
                ContractHash = dto.ContractHash ?? "",
                DefinitionHash = dto.DefinitionHash ?? "",
                Version = dto.Version ?? 0,
                Schema = dto.FormSchema is { } fschema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(fschema.Id, fschema.Version ?? 1)
                    : default,
            });
    }

    private static DraftAbstractions.DescriptorDraftPayload MapToEventPayload(
        AgentEventDraftPayloadDto dto)
    {
        return new DraftAbstractions.EventDescriptorDraftPayload(
            new Event.Abstractions.EventDescriptor
            {
                Id = dto.DescriptorRef?.Id ?? dto.Name ?? "",
                Name = dto.Name ?? "",
                State = Enum.TryParse<DescriptorState>(dto.State, out var state)
                    ? state
                    : DescriptorState.Active,
                ContractHash = dto.ContractHash ?? "",
                DefinitionHash = dto.DefinitionHash ?? "",
                Version = dto.Version ?? 0,
                Category = Enum.TryParse<Event.Abstractions.EventCategory>(
                    dto.EventKind, out var category)
                    ? category
                    : default,
                Semantic = Enum.TryParse<Event.Abstractions.EventSemantic>(
                    dto.EventType, out var semantic)
                    ? semantic
                    : default,
                PayloadSchema = dto.PayloadSchema is { } pschema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(pschema.Id, pschema.Version ?? 1)
                    : default,
                Importance = Enum.TryParse<Event.Abstractions.EventImportance>(
                    dto.Importance, out var importance)
                    ? importance
                    : default,
                ChangeKind = Enum.TryParse<Schema.Abstractions.SchemaChangeKind>(
                    dto.ChangeKind, out var changeKind)
                    ? changeKind
                    : default,
            });
    }

    private static DraftAbstractions.DescriptorDraftPayload MapToSchemaPayload(
        AgentSchemaDraftPayloadDto dto)
    {
        return new DraftAbstractions.SchemaDescriptorDraftPayload(
            new Schema.Abstractions.SchemaDescriptor
            {
                Id = dto.DescriptorRef?.Id ?? dto.Name ?? "",
                Name = dto.Name ?? "",
                State = Enum.TryParse<DescriptorState>(dto.State, out var state)
                    ? state
                    : DescriptorState.Active,
                ContractHash = dto.ContractHash ?? "",
                DefinitionHash = dto.DefinitionHash ?? "",
                Version = dto.Version ?? 0,
                ChangeKind = Enum.TryParse<Schema.Abstractions.SchemaChangeKind>(
                    dto.SchemaKind, out var changeKind)
                    ? changeKind
                    : default,
            });
    }

    // ── MergeToDomainPayload ────────────────────────────────────────

    /// <summary>
    /// Merges DTO fields into an existing domain payload, preserving sub-structures
    /// not represented in the DTO (Steps, Fields, ValidationRules, Outcomes, etc.).
    /// The DTO is a metadata-level contract — only DTO-covered fields are updated;
    /// all other domain properties are preserved from the existing payload.
    /// </summary>
    public static DraftAbstractions.DescriptorDraftPayload MergeToDomainPayload(
        DraftAbstractions.DescriptorDraftPayload existing,
        AgentDraftPayloadDto dto)
    {
        ValidateDiscriminator(dto);

        var existingDescriptor = existing.GetDescriptor();

        return dto.Discriminator switch
        {
            DescriptorKind.Capability => MergeCapabilityPayload(existingDescriptor, dto.Capability!),
            DescriptorKind.Workflow => MergeWorkflowPayload(existingDescriptor, dto.Workflow!),
            DescriptorKind.HumanTask => MergeHumanTaskPayload(existingDescriptor, dto.HumanTask!),
            DescriptorKind.Form => MergeFormPayload(existingDescriptor, dto.Form!),
            DescriptorKind.Event => MergeEventPayload(existingDescriptor, dto.Event!),
            DescriptorKind.Schema => MergeSchemaPayload(existingDescriptor, dto.Schema!),
            _ => throw new ArgumentOutOfRangeException(
                nameof(dto.Discriminator), dto.Discriminator, "Unsupported descriptor kind."),
        };
    }

    private static DraftAbstractions.DescriptorDraftPayload MergeCapabilityPayload(
        IDescriptor existing,
        AgentCapabilityDraftPayloadDto dto)
    {
        var existingCap = (CapabilityDescriptor)existing;
        return new DraftAbstractions.CapabilityDescriptorDraftPayload(
            new CapabilityDescriptor
            {
                Id = existingCap.Id,
                Name = dto.Name ?? existingCap.Name,
                State = Enum.TryParse<DescriptorState>(dto.State, out var state) ? state : existingCap.State,
                SupersededById = existingCap.SupersededById,
                ContractHash = dto.ContractHash ?? existingCap.ContractHash,
                DefinitionHash = dto.DefinitionHash ?? existingCap.DefinitionHash,
                Version = dto.Version ?? existingCap.Version,
                InputSchema = dto.InputSchema is { } ischema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(ischema.Id, ischema.Version ?? 1)
                    : existingCap.InputSchema,
                OutputSchema = dto.OutputSchema is { } oschema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(oschema.Id, oschema.Version ?? 1)
                    : existingCap.OutputSchema,
                CapabilityKind = Enum.TryParse<Metadata.Abstractions.CapabilityKind>(dto.CapabilityKind, out var capKind)
                    ? capKind
                    : existingCap.CapabilityKind,
                Categories = dto.Categories ?? existingCap.Categories.ToArray(),
                Produces = dto.Produces is not null
                    ? dto.Produces.Select(r => new EventRef(r.Namespace, r.Id, r.Version)).ToArray()
                    : existingCap.Produces.ToArray(),
                Consumes = dto.Consumes is not null
                    ? dto.Consumes.Select(r => new EventRef(r.Namespace, r.Id, r.Version)).ToArray()
                    : existingCap.Consumes.ToArray(),
                SemanticTags = dto.SemanticTags ?? existingCap.SemanticTags.ToArray(),
                Permissions = dto.Permissions ?? existingCap.Permissions.ToArray(),
                RiskLevel = Enum.TryParse<Metadata.Abstractions.CapabilityRiskLevel>(dto.RiskLevel, out var riskLevel)
                    ? riskLevel
                    : existingCap.RiskLevel,
            });
    }

    private static DraftAbstractions.DescriptorDraftPayload MergeWorkflowPayload(
        IDescriptor existing,
        AgentWorkflowDraftPayloadDto dto)
    {
        var existingWf = (Workflow.Abstractions.WorkflowDescriptor)existing;
        return new DraftAbstractions.WorkflowDescriptorDraftPayload(
            new Workflow.Abstractions.WorkflowDescriptor
            {
                Id = existingWf.Id,
                Name = dto.Name ?? existingWf.Name,
                State = Enum.TryParse<DescriptorState>(dto.State, out var state) ? state : existingWf.State,
                SupersededById = existingWf.SupersededById,
                ContractHash = dto.ContractHash ?? existingWf.ContractHash,
                DefinitionHash = dto.DefinitionHash ?? existingWf.DefinitionHash,
                Version = dto.Version ?? existingWf.Version,
                VariableSchema = dto.VariableSchema is { } vschema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(vschema.Id, vschema.Version ?? 1)
                    : existingWf.VariableSchema,
                Steps = existingWf.Steps,
                DefaultVariableScope = existingWf.DefaultVariableScope,
            });
    }

    private static DraftAbstractions.DescriptorDraftPayload MergeHumanTaskPayload(
        IDescriptor existing,
        AgentHumanTaskDraftPayloadDto dto)
    {
        var existingHt = (HumanTask.Abstractions.HumanTaskDescriptor)existing;
        return new DraftAbstractions.HumanTaskDescriptorDraftPayload(
            new HumanTask.Abstractions.HumanTaskDescriptor
            {
                Id = existingHt.Id,
                Name = dto.Name ?? existingHt.Name,
                State = Enum.TryParse<DescriptorState>(dto.State, out var state) ? state : existingHt.State,
                SupersededById = existingHt.SupersededById,
                ContractHash = dto.ContractHash ?? existingHt.ContractHash,
                DefinitionHash = dto.DefinitionHash ?? existingHt.DefinitionHash,
                Version = dto.Version ?? existingHt.Version,
                InputSchema = dto.InputSchema is { } ischema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(ischema.Id, ischema.Version ?? 1)
                    : existingHt.InputSchema,
                OutputSchema = dto.OutputSchema is { } oschema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(oschema.Id, oschema.Version ?? 1)
                    : existingHt.OutputSchema,
                Interaction = dto.Interaction is { } interaction
                    ? new VersionedDescriptorRef<IInteractionDescriptor>(interaction.Id, interaction.Version ?? 1)
                    : existingHt.Interaction,
                Timeout = dto.Timeout is not null && TimeSpan.TryParse(dto.Timeout, out var timeout)
                    ? timeout
                    : existingHt.Timeout,
                AssigneeStrategy = Enum.TryParse<HumanTask.Abstractions.AssigneeStrategy>(dto.AssignmentStrategy, out var strategy)
                    ? strategy
                    : existingHt.AssigneeStrategy,
                Permissions = existingHt.Permissions,
                Outcomes = existingHt.Outcomes,
            });
    }

    private static DraftAbstractions.DescriptorDraftPayload MergeFormPayload(
        IDescriptor existing,
        AgentFormDraftPayloadDto dto)
    {
        var existingForm = (Form.Abstractions.FormDescriptor)existing;
        return new DraftAbstractions.FormDescriptorDraftPayload(
            new Form.Abstractions.FormDescriptor
            {
                Id = existingForm.Id,
                Name = dto.Name ?? existingForm.Name,
                State = Enum.TryParse<DescriptorState>(dto.State, out var state) ? state : existingForm.State,
                SupersededById = existingForm.SupersededById,
                ContractHash = dto.ContractHash ?? existingForm.ContractHash,
                DefinitionHash = dto.DefinitionHash ?? existingForm.DefinitionHash,
                Version = dto.Version ?? existingForm.Version,
                Schema = dto.FormSchema is { } fschema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(fschema.Id, fschema.Version ?? 1)
                    : existingForm.Schema,
                Fields = existingForm.Fields,
                LayoutColumns = existingForm.LayoutColumns,
            });
    }

    private static DraftAbstractions.DescriptorDraftPayload MergeEventPayload(
        IDescriptor existing,
        AgentEventDraftPayloadDto dto)
    {
        var existingEvt = (Event.Abstractions.EventDescriptor)existing;
        return new DraftAbstractions.EventDescriptorDraftPayload(
            new Event.Abstractions.EventDescriptor
            {
                Id = existingEvt.Id,
                Name = dto.Name ?? existingEvt.Name,
                State = Enum.TryParse<DescriptorState>(dto.State, out var state) ? state : existingEvt.State,
                SupersededById = existingEvt.SupersededById,
                ContractHash = dto.ContractHash ?? existingEvt.ContractHash,
                DefinitionHash = dto.DefinitionHash ?? existingEvt.DefinitionHash,
                Version = dto.Version ?? existingEvt.Version,
                PayloadSchema = dto.PayloadSchema is { } pschema
                    ? new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>(pschema.Id, pschema.Version ?? 1)
                    : existingEvt.PayloadSchema,
                Importance = Enum.TryParse<Event.Abstractions.EventImportance>(dto.Importance, out var importance)
                    ? importance
                    : existingEvt.Importance,
                ChangeKind = Enum.TryParse<Schema.Abstractions.SchemaChangeKind>(dto.ChangeKind, out var changeKind)
                    ? changeKind
                    : existingEvt.ChangeKind,
                Category = Enum.TryParse<Event.Abstractions.EventCategory>(dto.EventKind, out var category)
                    ? category
                    : existingEvt.Category,
                Semantic = Enum.TryParse<Event.Abstractions.EventSemantic>(dto.EventType, out var semantic)
                    ? semantic
                    : existingEvt.Semantic,
            });
    }

    private static DraftAbstractions.DescriptorDraftPayload MergeSchemaPayload(
        IDescriptor existing,
        AgentSchemaDraftPayloadDto dto)
    {
        var existingSchema = (Schema.Abstractions.SchemaDescriptor)existing;
        return new DraftAbstractions.SchemaDescriptorDraftPayload(
            new Schema.Abstractions.SchemaDescriptor
            {
                Id = existingSchema.Id,
                Name = dto.Name ?? existingSchema.Name,
                State = Enum.TryParse<DescriptorState>(dto.State, out var state) ? state : existingSchema.State,
                SupersededById = existingSchema.SupersededById,
                ContractHash = dto.ContractHash ?? existingSchema.ContractHash,
                DefinitionHash = dto.DefinitionHash ?? existingSchema.DefinitionHash,
                Version = dto.Version ?? existingSchema.Version,
                ChangeKind = Enum.TryParse<Schema.Abstractions.SchemaChangeKind>(dto.SchemaKind, out var changeKind)
                    ? changeKind
                    : existingSchema.ChangeKind,
                Fields = existingSchema.Fields,
                ValidationRules = existingSchema.ValidationRules,
                References = existingSchema.References,
            });
    }
}
