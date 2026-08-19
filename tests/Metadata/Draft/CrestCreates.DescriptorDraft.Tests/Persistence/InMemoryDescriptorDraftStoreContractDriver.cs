using System.Collections.Immutable;
using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.DescriptorDraft.Tests.Persistence;

internal sealed class InMemoryDescriptorDraftStoreContractDriver : IDescriptorDraftStoreContractDriver
{
    private readonly InMemoryDescriptorDraftStore _store = new();
    private readonly DefaultDescriptorDraftValidator _validator = new();

    public IDescriptorDraftStore Store => _store;
    public IDescriptorDraftValidator Validator => _validator;

    public Draft CreatePayloadVariant(DescriptorPayloadVariant variant) => variant switch
    {
        DescriptorPayloadVariant.Schema => CreateSchemaDraft(),
        DescriptorPayloadVariant.Form => CreateFormDraft(),
        DescriptorPayloadVariant.Capability => CreateCapabilityDraft(),
        DescriptorPayloadVariant.HumanTask => CreateHumanTaskDraft(),
        DescriptorPayloadVariant.Event => CreateEventDraft(),
        DescriptorPayloadVariant.WorkflowCapabilityTarget => CreateWorkflowDraft("capability"),
        DescriptorPayloadVariant.WorkflowHumanTaskTarget => CreateWorkflowDraft("humantask"),
        DescriptorPayloadVariant.WorkflowSubWorkflowTarget => CreateWorkflowDraft("subworkflow"),
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown variant")
    };

    public DescriptorPayloadObservation ObservePayload(Draft draft, DescriptorPayloadVariant variant)
    {
        var leaves = Header(draft);
        switch (draft.Payload)
        {
            case SchemaDescriptorDraftPayload payload:
                AddSchema(leaves, payload.Descriptor);
                break;
            case FormDescriptorDraftPayload payload:
                AddForm(leaves, payload.Descriptor);
                break;
            case CapabilityDescriptorDraftPayload payload:
                AddCapability(leaves, payload.Descriptor);
                break;
            case HumanTaskDescriptorDraftPayload payload:
                AddHumanTask(leaves, payload.Descriptor);
                break;
            case EventDescriptorDraftPayload payload:
                AddEvent(leaves, payload.Descriptor);
                break;
            case WorkflowDescriptorDraftPayload payload:
                AddWorkflow(leaves, payload.Descriptor);
                break;
            default:
                throw new InvalidOperationException($"Unsupported payload type {draft.Payload.GetType().Name}.");
        }

        return new DescriptorPayloadObservation(variant, leaves.ToImmutableArray());
    }

    public Draft CreateValidatorOwnedInvalid(DraftValidatorOwnedInvalidVariant variant) => variant switch
    {
        DraftValidatorOwnedInvalidVariant.DraftIdBlank => CreateDraftWithId(""),
        DraftValidatorOwnedInvalidVariant.DescriptorIdBlank => CreateDraftWithDescriptorId(""),
        DraftValidatorOwnedInvalidVariant.AuthorIdBlank => CreateDraftWithAuthorId(""),
        DraftValidatorOwnedInvalidVariant.SupportedPayloadKindMismatch => CreateMismatchDraft(),
        DraftValidatorOwnedInvalidVariant.DefinedNonPayloadKindMismatch => CreateNonPayloadKindDraft(),
        DraftValidatorOwnedInvalidVariant.PayloadIdMismatch => CreatePayloadIdMismatchDraft(),
        DraftValidatorOwnedInvalidVariant.ProposedVersionMissing => CreateDraftWithOperation(DescriptorDraftOperation.Create),
        DraftValidatorOwnedInvalidVariant.ProposedVersionNotInteger => CreateDraftWithProposedVersion("not-a-number"),
        DraftValidatorOwnedInvalidVariant.ProposedVersionMismatch => CreateDraftWithMismatchedVersions(),
        DraftValidatorOwnedInvalidVariant.CreateBaseVersionPresent => CreateDraftWithBaseVersion(DescriptorDraftOperation.Create, "1"),
        DraftValidatorOwnedInvalidVariant.UpdateBaseVersionMissing => CreateDraftWithOperation(DescriptorDraftOperation.Update),
        DraftValidatorOwnedInvalidVariant.DeprecateBaseVersionMissing => CreateDraftWithOperation(DescriptorDraftOperation.Deprecate),
        DraftValidatorOwnedInvalidVariant.RemoveBaseVersionMissing => CreateDraftWithOperation(DescriptorDraftOperation.Remove),
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown variant")
    };

    public Draft CreateValidatorOwnedInvalid(DraftValidatorOwnedInvalidVariant variant, EvidenceVectorKey key)
    {
        return variant switch
        {
            DraftValidatorOwnedInvalidVariant.DescriptorIdBlank => CreateSchemaDraft() with
            {
                DescriptorId = key switch
                {
                    EvidenceVectorKey.Null => null!,
                    EvidenceVectorKey.Empty => string.Empty,
                    EvidenceVectorKey.Whitespace => "   ",
                    _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported DescriptorId vector")
                }
            },
            DraftValidatorOwnedInvalidVariant.AuthorIdBlank => CreateSchemaDraft() with
            {
                AuthorId = key switch
                {
                    EvidenceVectorKey.Null => null!,
                    EvidenceVectorKey.Empty => string.Empty,
                    EvidenceVectorKey.Whitespace => "   ",
                    _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported AuthorId vector")
                }
            },
            DraftValidatorOwnedInvalidVariant.DefinedNonPayloadKindMismatch => CreateSchemaDraft() with
            {
                DescriptorKind = key switch
                {
                    EvidenceVectorKey.Unknown => DescriptorKind.Unknown,
                    EvidenceVectorKey.DynamicApiEndpoint => DescriptorKind.DynamicApiEndpoint,
                    EvidenceVectorKey.McpTool => DescriptorKind.McpTool,
                    EvidenceVectorKey.AgentTool => DescriptorKind.AgentTool,
                    _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported DescriptorKind vector")
                }
            },
            DraftValidatorOwnedInvalidVariant.ProposedVersionMissing =>
                CreateDraftWithOperation(key == EvidenceVectorKey.Create
                    ? DescriptorDraftOperation.Create
                    : DescriptorDraftOperation.Update),
            _ => CreateValidatorOwnedInvalid(variant)
        };
    }

    public ValueTask ResetAsync() => ValueTask.CompletedTask;

    private static List<DescriptorPayloadObservationLeaf> Header(Draft draft)
    {
        var leaves = new List<DescriptorPayloadObservationLeaf>
        {
            new("TenantId", ObservationValueKind.Text, draft.TenantId, null, null, null),
            new("DraftId", ObservationValueKind.Text, draft.DraftId, null, null, null),
            new("DescriptorKind", ObservationValueKind.EnumUnderlyingValue, null, (int)draft.DescriptorKind, null, null),
            new("CreatedAt.UtcTicks", ObservationValueKind.Ticks, null, draft.CreatedAt.UtcTicks, null, null)
        };
        return leaves;
    }

    private static void AddSchema(List<DescriptorPayloadObservationLeaf> leaves, SchemaDescriptor descriptor)
    {
        Text(leaves, "Payload.Id", descriptor.Id);
        Text(leaves, "Payload.Name", descriptor.Name);
        Enum(leaves, "Payload.ChangeKind", (int)descriptor.ChangeKind);
        Integer(leaves, "Payload.Fields.Count", descriptor.Fields.Count);
        var field = descriptor.Fields[0];
        Text(leaves, "Payload.Fields[0].Name", field.Name);
        Text(leaves, "Payload.Fields[0].FieldType", field.FieldType);
        var reference = field.ObjectSchema ?? throw new InvalidOperationException("Schema fixture is missing ObjectSchema.");
        Text(leaves, "Payload.Fields[0].ObjectSchema.Id", reference.Id);
        Integer(leaves, "Payload.Fields[0].ObjectSchema.Version", reference.Version);
        Enum(leaves, "Payload.Fields[0].ObjectSchema.SelectionMode", (int)reference.SelectionMode);
        Text(leaves, "Payload.Fields[0].ObjectSchema.ExpectedContractHash", reference.ExpectedContractHash!);
        Integer(leaves, "Payload.References.Count", descriptor.References.Count);
        Text(leaves, "Payload.References[0].Id", descriptor.References[0].Id);
        Integer(leaves, "Payload.References[0].Version", descriptor.References[0].Version);
    }

    private static void AddForm(List<DescriptorPayloadObservationLeaf> leaves, FormDescriptor descriptor)
    {
        Text(leaves, "Payload.Id", descriptor.Id);
        Text(leaves, "Payload.Name", descriptor.Name);
        Text(leaves, "Payload.Schema.Id", descriptor.Schema.Id);
        Integer(leaves, "Payload.Schema.Version", descriptor.Schema.Version);
        Integer(leaves, "Payload.Fields.Count", descriptor.Fields.Count);
        var field = descriptor.Fields[0];
        Text(leaves, "Payload.Fields[0].SchemaFieldName", field.SchemaFieldName);
        Text(leaves, "Payload.Fields[0].Label", field.Label!);
        Text(leaves, "Payload.Fields[0].Metadata[display]", field.Metadata["display"]);
    }

    private static void AddCapability(List<DescriptorPayloadObservationLeaf> leaves, CapabilityDescriptor descriptor)
    {
        Text(leaves, "Payload.Id", descriptor.Id);
        Text(leaves, "Payload.Name", descriptor.Name);
        Text(leaves, "Payload.Namespace", descriptor.Namespace);
        Text(leaves, "Payload.Categories[0]", descriptor.Categories[0]);
        var produced = descriptor.Produces[0];
        Text(leaves, "Payload.Produces[0].Namespace", produced.Namespace);
        Text(leaves, "Payload.Produces[0].Id", produced.Id);
        Integer(leaves, "Payload.Produces[0].Version", produced.Version!.Value);
        var consumed = descriptor.Consumes[0];
        Text(leaves, "Payload.Consumes[0].Namespace", consumed.Namespace);
        Text(leaves, "Payload.Consumes[0].Id", consumed.Id);
        if (consumed.Version is null)
            leaves.Add(new("Payload.Consumes[0].Version", ObservationValueKind.Null, null, null, null, null));
        else
            Integer(leaves, "Payload.Consumes[0].Version", consumed.Version.Value);
        Text(leaves, "Payload.SemanticTags[0]", descriptor.SemanticTags[0]);
        Enum(leaves, "Payload.CapabilityKind", (int)descriptor.CapabilityKind);
        Text(leaves, "Payload.InputSchema.Id", descriptor.InputSchema!.Value.Id);
        Integer(leaves, "Payload.InputSchema.Version", descriptor.InputSchema.Value.Version);
        Text(leaves, "Payload.OutputSchema.Id", descriptor.OutputSchema!.Value.Id);
        Integer(leaves, "Payload.OutputSchema.Version", descriptor.OutputSchema.Value.Version);
        Text(leaves, "Payload.Permissions[0]", descriptor.Permissions[0]);
        Enum(leaves, "Payload.RiskLevel", (int)descriptor.RiskLevel);
        Enum(leaves, "Payload.ProjectionKind", (int)descriptor.ProjectionKind);
    }

    private static void AddHumanTask(List<DescriptorPayloadObservationLeaf> leaves, HumanTaskDescriptor descriptor)
    {
        Text(leaves, "Payload.Id", descriptor.Id);
        Text(leaves, "Payload.Name", descriptor.Name);
        Text(leaves, "Payload.Interaction.Id", descriptor.Interaction.Id);
        Integer(leaves, "Payload.Interaction.Version", descriptor.Interaction.Version);
        Text(leaves, "Payload.InputSchema.Id", descriptor.InputSchema!.Value.Id);
        Integer(leaves, "Payload.InputSchema.Version", descriptor.InputSchema.Value.Version);
        Ticks(leaves, "Payload.Timeout.Ticks", descriptor.Timeout!.Value.Ticks);
        Text(leaves, "Payload.Permissions", descriptor.Permissions!);
        Integer(leaves, "Payload.Outcomes.Count", descriptor.Outcomes.Count);
        var outcome = descriptor.Outcomes[0];
        Enum(leaves, "Payload.Outcomes[0].Condition", (int)outcome.Condition);
        Text(leaves, "Payload.Outcomes[0].Capability.Id", outcome.Capability!.Value.Id);
        Integer(leaves, "Payload.Outcomes[0].Capability.Version", outcome.Capability.Value.Version);
    }

    private static void AddEvent(List<DescriptorPayloadObservationLeaf> leaves, EventDescriptor descriptor)
    {
        Text(leaves, "Payload.Id", descriptor.Id);
        Text(leaves, "Payload.Name", descriptor.Name);
        Text(leaves, "Payload.PayloadSchema.Id", descriptor.PayloadSchema.Id);
        Integer(leaves, "Payload.PayloadSchema.Version", descriptor.PayloadSchema.Version);
        Enum(leaves, "Payload.Category", (int)descriptor.Category);
        Enum(leaves, "Payload.Semantic", (int)descriptor.Semantic);
        Enum(leaves, "Payload.Importance", (int)descriptor.Importance);
        Enum(leaves, "Payload.ChangeKind", (int)descriptor.ChangeKind);
    }

    private static void AddWorkflow(List<DescriptorPayloadObservationLeaf> leaves, WorkflowDescriptor descriptor)
    {
        Text(leaves, "Payload.Id", descriptor.Id);
        Text(leaves, "Payload.Name", descriptor.Name);
        Text(leaves, "Payload.VariableSchema.Id", descriptor.VariableSchema!.Value.Id);
        Integer(leaves, "Payload.VariableSchema.Version", descriptor.VariableSchema.Value.Version);
        Integer(leaves, "Payload.Steps.Count", descriptor.Steps.Count);
        var step = descriptor.Steps[0];
        Text(leaves, "Payload.Steps[0].Id", step.Id);
        Text(leaves, "Payload.Steps[0].Name", step.Name);
        var reference = step.Target switch
        {
            CapabilityTarget target => ("Capability", target.Capability!.Id, target.Capability.Version),
            HumanTaskTarget target => ("HumanTask", target.HumanTask!.Id, target.HumanTask.Version),
            SubWorkflowTarget target => ("SubWorkflow", target.SubWorkflow!.Id, target.SubWorkflow.Version),
            _ => throw new InvalidOperationException($"Unsupported workflow target {step.Target.GetType().Name}.")
        };
        Text(leaves, "Payload.Steps[0].Target.Kind", reference.Item1);
        Text(leaves, "Payload.Steps[0].Target.Reference.Id", reference.Item2);
        Integer(leaves, "Payload.Steps[0].Target.Reference.Version", reference.Item3);
        Text(leaves, "Payload.Steps[0].Condition", step.Condition!);
        Text(leaves, "Payload.Steps[0].Transitions[0]", step.Transitions[0]);
        Text(leaves, "Payload.Steps[0].InputMapping", step.InputMapping!);
        Text(leaves, "Payload.Steps[0].OutputMapping", step.OutputMapping!);
        Enum(leaves, "Payload.Steps[0].OnError", (int)step.OnError);
        Enum(leaves, "Payload.DefaultVariableScope", (int)descriptor.DefaultVariableScope);
    }

    private static void Text(List<DescriptorPayloadObservationLeaf> leaves, string path, string value)
        => leaves.Add(new(path, ObservationValueKind.Text, value, null, null, null));

    private static void Integer(List<DescriptorPayloadObservationLeaf> leaves, string path, long value)
        => leaves.Add(new(path, ObservationValueKind.Integer, null, value, null, null));

    private static void Enum(List<DescriptorPayloadObservationLeaf> leaves, string path, long value)
        => leaves.Add(new(path, ObservationValueKind.EnumUnderlyingValue, null, value, null, null));

    private static void Ticks(List<DescriptorPayloadObservationLeaf> leaves, string path, long value)
        => leaves.Add(new(path, ObservationValueKind.Ticks, null, value, null, null));

    private static Draft CreateSchemaDraft() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-Schema",
        DescriptorKind = DescriptorKind.Schema,
        DescriptorId = "Schema-descriptor",
        Operation = DescriptorDraftOperation.Update,
        AuthorKind = DescriptorDraftAuthorKind.Agent,
        AuthorId = "agent-1",
        CreatedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(5)),
        BaseVersion = "1",
        ProposedVersion = "2",
        Intent = "test",
        Rationale = "codec",
        CorrelationId = "correlation-1",
        Source = "test",
        Metadata = new Dictionary<string, string> { ["key"] = "value" },
        Status = DescriptorDraftStatus.Reviewed,
        Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor
        {
            Id = "schema-1",
            Name = "Schema",
            ChangeKind = SchemaChangeKind.Additive,
            Fields = new[]
            {
                new SchemaFieldDescriptor
                {
                    Name = "Address",
                    FieldType = "object",
                    ObjectSchema = new VersionedDescriptorRef<SchemaDescriptor>
                    {
                        Id = "address",
                        Version = 2,
                        SelectionMode = VersionSelectionMode.Latest,
                        ExpectedContractHash = "hash"
                    }
                }
            },
            References = new[]
            {
                new VersionedDescriptorRef<SchemaDescriptor> { Id = "address", Version = 2 }
            }
        })
    };

    private static Draft CreateFormDraft() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-Form",
        DescriptorKind = DescriptorKind.Form,
        DescriptorId = "Form-descriptor",
        Operation = DescriptorDraftOperation.Update,
        AuthorKind = DescriptorDraftAuthorKind.Agent,
        AuthorId = "agent-1",
        CreatedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(5)),
        BaseVersion = "1",
        ProposedVersion = "2",
        Intent = "test",
        Rationale = "codec",
        CorrelationId = "correlation-1",
        Source = "test",
        Metadata = new Dictionary<string, string> { ["key"] = "value" },
        Status = DescriptorDraftStatus.Reviewed,
        Payload = new FormDescriptorDraftPayload(new CrestCreates.Form.Abstractions.FormDescriptor
        {
            Id = "form-1",
            Name = "Form",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>
            {
                Id = "schema-1",
                Version = 1,
            },
            Fields = new[]
            {
                new CrestCreates.Form.Abstractions.FormFieldDescriptor
                {
                    SchemaFieldName = "Address",
                    Label = "Address",
                    Metadata = new Dictionary<string, string> { ["display"] = "compact" }
                }
            }
        })
    };

    private static Draft CreateCapabilityDraft() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-Capability",
        DescriptorKind = DescriptorKind.Capability,
        DescriptorId = "Capability-descriptor",
        Operation = DescriptorDraftOperation.Update,
        AuthorKind = DescriptorDraftAuthorKind.Agent,
        AuthorId = "agent-1",
        CreatedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(5)),
        BaseVersion = "1",
        ProposedVersion = "2",
        Intent = "test",
        Rationale = "codec",
        CorrelationId = "correlation-1",
        Source = "test",
        Metadata = new Dictionary<string, string> { ["key"] = "value" },
        Status = DescriptorDraftStatus.Reviewed,
        Payload = new CapabilityDescriptorDraftPayload(new CrestCreates.Metadata.CapabilityDescriptor
        {
            Id = "capability-1",
            Name = "Capability",
            Categories = new[] { "read" },
            Produces = new[] { new EventRef("event", "changed", 3) },
            Consumes = new[] { new EventRef("event", "created") },
            SemanticTags = new[] { "safe" },
            CapabilityKind = CapabilityKind.Query,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "input", Version = 1 },
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "output", Version = 1 },
            Permissions = new[] { "read:capability" },
            RiskLevel = CapabilityRiskLevel.High,
            ProjectionKind = CapabilityProjectionKind.AppServiceCompatibility
        })
    };

    private static Draft CreateHumanTaskDraft() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-HumanTask",
        DescriptorKind = DescriptorKind.HumanTask,
        DescriptorId = "HumanTask-descriptor",
        Operation = DescriptorDraftOperation.Update,
        AuthorKind = DescriptorDraftAuthorKind.Agent,
        AuthorId = "agent-1",
        CreatedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(5)),
        BaseVersion = "1",
        ProposedVersion = "2",
        Intent = "test",
        Rationale = "codec",
        CorrelationId = "correlation-1",
        Source = "test",
        Metadata = new Dictionary<string, string> { ["key"] = "value" },
        Status = DescriptorDraftStatus.Reviewed,
        Payload = new HumanTaskDescriptorDraftPayload(new CrestCreates.HumanTask.Abstractions.HumanTaskDescriptor
        {
            Id = "task-1",
            Name = "Task",
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor> { Id = "form-1", Version = 1 },
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "input", Version = 1 },
            Timeout = TimeSpan.FromMinutes(5),
            Permissions = "approve",
            Outcomes = new[]
            {
                new CompletionOutcome
                {
                    Condition = CompletionCondition.Approve,
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "capability-1", Version = 1 }
                }
            }
        })
    };

    private static Draft CreateEventDraft() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-Event",
        DescriptorKind = DescriptorKind.Event,
        DescriptorId = "Event-descriptor",
        Operation = DescriptorDraftOperation.Update,
        AuthorKind = DescriptorDraftAuthorKind.Agent,
        AuthorId = "agent-1",
        CreatedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(5)),
        BaseVersion = "1",
        ProposedVersion = "2",
        Intent = "test",
        Rationale = "codec",
        CorrelationId = "correlation-1",
        Source = "test",
        Metadata = new Dictionary<string, string> { ["key"] = "value" },
        Status = DescriptorDraftStatus.Reviewed,
        Payload = new EventDescriptorDraftPayload(new CrestCreates.Event.Abstractions.EventDescriptor
        {
            Id = "event-1",
            Name = "Event",
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("payload", 1),
            Category = EventCategory.Domain,
            Semantic = EventSemantic.Fact,
            Importance = EventImportance.Business,
            ChangeKind = SchemaChangeKind.Breaking
        })
    };

    private static Draft CreateWorkflowDraft(string targetKind) => new()
    {
        TenantId = "tenant-1",
        DraftId = targetKind switch
        {
            "capability" => "draft-workflow-Capability",
            "humantask" => "draft-workflow-HumanTask",
            "subworkflow" => "draft-workflow-SubWorkflow",
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, null)
        },
        DescriptorKind = DescriptorKind.Workflow,
        DescriptorId = "Workflow-descriptor",
        Operation = DescriptorDraftOperation.Update,
        AuthorKind = DescriptorDraftAuthorKind.Human,
        AuthorId = "author-1",
        CreatedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(5)),
        BaseVersion = "1",
        ProposedVersion = "2",
        Intent = "test",
        Rationale = "codec",
        CorrelationId = "correlation-1",
        Source = "test",
        Metadata = new Dictionary<string, string> { ["key"] = "value" },
        Status = DescriptorDraftStatus.Created,
        Payload = new WorkflowDescriptorDraftPayload(new CrestCreates.Workflow.Abstractions.WorkflowDescriptor
        {
            Id = "workflow-1",
            Name = "Workflow",
            VariableSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "variables", Version = 1 },
            Steps = new[]
            {
                new CrestCreates.Workflow.Abstractions.WorkflowStep
                {
                    Id = "step-1",
                    Name = "Step",
                    Target = targetKind switch
                    {
                        "capability" => new CrestCreates.Workflow.Abstractions.CapabilityTarget
                        {
                            Capability = new VersionedDescriptorRef<IVersionedDescriptor>
                            {
                                Id = "capability-1",
                                Version = 1
                            }
                        },
                        "humantask" => new CrestCreates.Workflow.Abstractions.HumanTaskTarget
                        {
                            HumanTask = new VersionedDescriptorRef<CrestCreates.HumanTask.Abstractions.HumanTaskDescriptor>
                            {
                                Id = "task-1",
                                Version = 1
                            }
                        },
                        "subworkflow" => new CrestCreates.Workflow.Abstractions.SubWorkflowTarget
                        {
                            SubWorkflow = new VersionedDescriptorRef<CrestCreates.Workflow.Abstractions.WorkflowDescriptor>
                            {
                                Id = "workflow-child",
                                Version = 1
                            }
                        },
                        _ => throw new ArgumentOutOfRangeException()
                    },
                    Condition = "ready",
                    Transitions = new[] { "step-2" },
                    InputMapping = "input",
                    OutputMapping = "output",
                    OnError = CrestCreates.Workflow.Abstractions.StepErrorBehavior.Skip
                }
            },
            DefaultVariableScope = CrestCreates.Workflow.Abstractions.WorkflowVariableScope.SubWorkflow
        })
    };

    private static Draft CreateDraftWithId(string draftId) => CreateSchemaDraft() with { DraftId = draftId };
    private static Draft CreateDraftWithDescriptorId(string descriptorId) => CreateSchemaDraft() with { DescriptorId = descriptorId };
    private static Draft CreateDraftWithAuthorId(string authorId) => CreateSchemaDraft() with { AuthorId = authorId };

    private static Draft CreateMismatchDraft() => CreateSchemaDraft() with
    {
        DraftId = "draft-mismatch-001",
        DescriptorKind = DescriptorKind.Workflow
    };

    private static Draft CreateNonPayloadKindDraft() => CreateSchemaDraft() with
    {
        DraftId = "draft-nonpayload-001",
        DescriptorKind = DescriptorKind.DynamicApiEndpoint
    };

    private static Draft CreatePayloadIdMismatchDraft() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-idmismatch-001",
        DescriptorKind = DescriptorKind.Schema,
        DescriptorId = "schema-desc-001",
        Operation = DescriptorDraftOperation.Create,
        AuthorKind = DescriptorDraftAuthorKind.Human,
        AuthorId = "author-001",
        CreatedAt = DateTimeOffset.UtcNow,
        Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor { Id = "different-schema-id", Name = "Mismatch" })
    };

    private static Draft CreateDraftWithProposedVersion(string proposedVersion) => CreateSchemaDraft() with
    {
        DraftId = "draft-version-001",
        ProposedVersion = proposedVersion
    };

    private static Draft CreateDraftWithMismatchedVersions() => CreateSchemaDraft() with
    {
        DraftId = "draft-mismatch-ver-001",
        Operation = DescriptorDraftOperation.Update,
        BaseVersion = "1",
        ProposedVersion = "5",
        Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor { Id = "schema-001", Name = "Test", Version = 1 })
    };

    private static Draft CreateDraftWithOperation(DescriptorDraftOperation operation) => CreateSchemaDraft() with
    {
        DraftId = "draft-operation-001",
        Operation = operation
    };

    private static Draft CreateDraftWithBaseVersion(DescriptorDraftOperation operation, string baseVersion) => CreateSchemaDraft() with
    {
        DraftId = "draft-basever-001",
        Operation = operation,
        BaseVersion = baseVersion,
        ProposedVersion = "2"
    };
}
