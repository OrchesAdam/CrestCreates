using System.Collections.Immutable;
using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
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
        var leaves = new List<DescriptorPayloadObservationLeaf>();
        leaves.Add(new("TenantId", ObservationValueKind.Text, draft.TenantId, null, null, null));
        leaves.Add(new("DraftId", ObservationValueKind.Text, draft.DraftId, null, null, null));
        leaves.Add(new("DescriptorKind", ObservationValueKind.EnumUnderlyingValue, null, (int)draft.DescriptorKind, null, null));
        leaves.Add(new("CreatedAt.UtcTicks", ObservationValueKind.Ticks, null, draft.CreatedAt.UtcTicks, null, null));
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

    public ValueTask ResetAsync() => ValueTask.CompletedTask;

    private static Draft CreateSchemaDraft() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-schema-001",
        DescriptorKind = DescriptorKind.Schema,
        DescriptorId = "schema-desc-001",
        Operation = DescriptorDraftOperation.Create,
        AuthorKind = DescriptorDraftAuthorKind.Human,
        AuthorId = "author-001",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor
        {
            Id = "schema-001",
            Name = "Test Schema",
            Fields = new[]
            {
                new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true },
                new SchemaFieldDescriptor
                {
                    Name = "Address",
                    FieldType = "object",
                    ObjectSchema = new VersionedDescriptorRef<SchemaDescriptor>
                    {
                        Id = "schema-address",
                        Version = 1,
                        SelectionMode = VersionSelectionMode.Exact
                    }
                }
            }
        })
    };

    private static Draft CreateFormDraft() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-form-001",
        DescriptorKind = DescriptorKind.Form,
        DescriptorId = "form-desc-001",
        Operation = DescriptorDraftOperation.Create,
        AuthorKind = DescriptorDraftAuthorKind.Human,
        AuthorId = "author-001",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Payload = new FormDescriptorDraftPayload(new CrestCreates.Form.Abstractions.FormDescriptor
        {
            Id = "form-001",
            Name = "Test Form",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>
            {
                Id = "schema-001",
                Version = 1,
                SelectionMode = VersionSelectionMode.Exact
            },
            Fields = new[]
            {
                new CrestCreates.Form.Abstractions.FormFieldDescriptor
                {
                    SchemaFieldName = "Name",
                    Label = "Name Label",
                    Order = 1
                }
            }
        })
    };

    private static Draft CreateCapabilityDraft() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-capability-001",
        DescriptorKind = DescriptorKind.Capability,
        DescriptorId = "capability-desc-001",
        Operation = DescriptorDraftOperation.Create,
        AuthorKind = DescriptorDraftAuthorKind.Human,
        AuthorId = "author-001",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Payload = new CapabilityDescriptorDraftPayload(new CrestCreates.Metadata.CapabilityDescriptor
        {
            Id = "capability-001",
            Name = "Test Capability"
        })
    };

    private static Draft CreateHumanTaskDraft() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-humantask-001",
        DescriptorKind = DescriptorKind.HumanTask,
        DescriptorId = "humantask-desc-001",
        Operation = DescriptorDraftOperation.Create,
        AuthorKind = DescriptorDraftAuthorKind.Human,
        AuthorId = "author-001",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Payload = new HumanTaskDescriptorDraftPayload(new CrestCreates.HumanTask.Abstractions.HumanTaskDescriptor
        {
            Id = "humantask-001",
            Name = "Test HumanTask"
        })
    };

    private static Draft CreateEventDraft() => new()
    {
        TenantId = "tenant-1",
        DraftId = "draft-event-001",
        DescriptorKind = DescriptorKind.Event,
        DescriptorId = "event-desc-001",
        Operation = DescriptorDraftOperation.Create,
        AuthorKind = DescriptorDraftAuthorKind.Human,
        AuthorId = "author-001",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Payload = new EventDescriptorDraftPayload(new CrestCreates.Event.Abstractions.EventDescriptor
        {
            Id = "event-001",
            Name = "Test Event"
        })
    };

    private static Draft CreateWorkflowDraft(string targetKind) => new()
    {
        TenantId = "tenant-1",
        DraftId = $"draft-workflow-{targetKind}-001",
        DescriptorKind = DescriptorKind.Workflow,
        DescriptorId = "workflow-desc-001",
        Operation = DescriptorDraftOperation.Create,
        AuthorKind = DescriptorDraftAuthorKind.Human,
        AuthorId = "author-001",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Payload = new WorkflowDescriptorDraftPayload(new CrestCreates.Workflow.Abstractions.WorkflowDescriptor
        {
            Id = "workflow-001",
            Name = "Test Workflow",
            Steps = new[]
            {
                new CrestCreates.Workflow.Abstractions.WorkflowStep
                {
                    Id = "step-1",
                    Name = "Step 1",
                    Target = targetKind switch
                    {
                        "capability" => new CrestCreates.Workflow.Abstractions.CapabilityTarget
                        {
                            Capability = new VersionedDescriptorRef<IVersionedDescriptor>
                            {
                                Id = "capability-001",
                                Version = 1
                            }
                        },
                        "humantask" => new CrestCreates.Workflow.Abstractions.HumanTaskTarget
                        {
                            HumanTask = new VersionedDescriptorRef<CrestCreates.HumanTask.Abstractions.HumanTaskDescriptor>
                            {
                                Id = "humantask-001",
                                Version = 1
                            }
                        },
                        "subworkflow" => new CrestCreates.Workflow.Abstractions.SubWorkflowTarget
                        {
                            SubWorkflow = new VersionedDescriptorRef<CrestCreates.Workflow.Abstractions.WorkflowDescriptor>
                            {
                                Id = "subworkflow-001",
                                Version = 1
                            }
                        },
                        _ => throw new ArgumentOutOfRangeException()
                    },
                    Transitions = new[] { "step-2" },
                    OnError = CrestCreates.Workflow.Abstractions.StepErrorBehavior.Fail
                }
            }
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
