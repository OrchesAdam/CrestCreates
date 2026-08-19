using System.Collections.Immutable;
using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

internal sealed class PostgreSqlDescriptorDraftStoreContractDriver : IDescriptorDraftStoreContractDriver
{
    public PostgreSqlDescriptorDraftStoreContractDriver(IDescriptorDraftStore store)
    {
        Store = store;
        Validator = new DefaultDescriptorDraftValidator();
    }

    public IDescriptorDraftStore Store { get; }
    public IDescriptorDraftValidator Validator { get; }

    public Draft CreatePayloadVariant(DescriptorPayloadVariant variant)
        => variant switch
        {
            DescriptorPayloadVariant.Schema => PostgreSqlControlPlaneReferenceDataJsonCodecTests.CreateDraft(DescriptorKind.Schema),
            DescriptorPayloadVariant.Form => PostgreSqlControlPlaneReferenceDataJsonCodecTests.CreateDraft(DescriptorKind.Form),
            DescriptorPayloadVariant.Capability => PostgreSqlControlPlaneReferenceDataJsonCodecTests.CreateDraft(DescriptorKind.Capability),
            DescriptorPayloadVariant.HumanTask => PostgreSqlControlPlaneReferenceDataJsonCodecTests.CreateDraft(DescriptorKind.HumanTask),
            DescriptorPayloadVariant.Event => PostgreSqlControlPlaneReferenceDataJsonCodecTests.CreateDraft(DescriptorKind.Event),
            DescriptorPayloadVariant.WorkflowCapabilityTarget => PostgreSqlControlPlaneReferenceDataJsonCodecTests.CreateWorkflowDraft(PostgreSqlWorkflowTargetType.Capability),
            DescriptorPayloadVariant.WorkflowHumanTaskTarget => PostgreSqlControlPlaneReferenceDataJsonCodecTests.CreateWorkflowDraft(PostgreSqlWorkflowTargetType.HumanTask),
            DescriptorPayloadVariant.WorkflowSubWorkflowTarget => PostgreSqlControlPlaneReferenceDataJsonCodecTests.CreateWorkflowDraft(PostgreSqlWorkflowTargetType.SubWorkflow),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
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

    public Draft CreateValidatorOwnedInvalid(DraftValidatorOwnedInvalidVariant variant)
        => variant switch
        {
            DraftValidatorOwnedInvalidVariant.DraftIdBlank => CreateSchema() with { DraftId = string.Empty },
            DraftValidatorOwnedInvalidVariant.DescriptorIdBlank => CreateSchema() with { DescriptorId = string.Empty },
            DraftValidatorOwnedInvalidVariant.AuthorIdBlank => CreateSchema() with { AuthorId = string.Empty },
            DraftValidatorOwnedInvalidVariant.SupportedPayloadKindMismatch => CreateSchema() with { DescriptorKind = DescriptorKind.Workflow },
            DraftValidatorOwnedInvalidVariant.DefinedNonPayloadKindMismatch => CreateSchema() with { DescriptorKind = DescriptorKind.DynamicApiEndpoint },
            DraftValidatorOwnedInvalidVariant.PayloadIdMismatch => CreateSchema() with
            {
                DescriptorId = "schema-desc-001",
                Payload = new SchemaDescriptorDraftPayload(new CrestCreates.Schema.Abstractions.SchemaDescriptor { Id = "different-schema-id", Name = "Mismatch" })
            },
            DraftValidatorOwnedInvalidVariant.ProposedVersionMissing => CreateSchema() with { Operation = DescriptorDraftOperation.Create },
            DraftValidatorOwnedInvalidVariant.ProposedVersionNotInteger => CreateSchema() with { ProposedVersion = "not-a-number" },
            DraftValidatorOwnedInvalidVariant.ProposedVersionMismatch => CreateSchema() with { Operation = DescriptorDraftOperation.Update, BaseVersion = "1", ProposedVersion = "5" },
            DraftValidatorOwnedInvalidVariant.CreateBaseVersionPresent => CreateSchema() with { Operation = DescriptorDraftOperation.Create, BaseVersion = "1", ProposedVersion = "2" },
            DraftValidatorOwnedInvalidVariant.UpdateBaseVersionMissing => CreateSchema() with { Operation = DescriptorDraftOperation.Update },
            DraftValidatorOwnedInvalidVariant.DeprecateBaseVersionMissing => CreateSchema() with { Operation = DescriptorDraftOperation.Deprecate },
            DraftValidatorOwnedInvalidVariant.RemoveBaseVersionMissing => CreateSchema() with { Operation = DescriptorDraftOperation.Remove },
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
        };

    public Draft CreateValidatorOwnedInvalid(DraftValidatorOwnedInvalidVariant variant, EvidenceVectorKey key)
        => variant switch
        {
            DraftValidatorOwnedInvalidVariant.DescriptorIdBlank => CreateSchema() with { DescriptorId = InvalidText(key) },
            DraftValidatorOwnedInvalidVariant.AuthorIdBlank => CreateSchema() with { AuthorId = InvalidText(key) },
            DraftValidatorOwnedInvalidVariant.DefinedNonPayloadKindMismatch => CreateSchema() with
            {
                DescriptorKind = key switch
                {
                    EvidenceVectorKey.Unknown => DescriptorKind.Unknown,
                    EvidenceVectorKey.DynamicApiEndpoint => DescriptorKind.DynamicApiEndpoint,
                    EvidenceVectorKey.McpTool => DescriptorKind.McpTool,
                    EvidenceVectorKey.AgentTool => DescriptorKind.AgentTool,
                    _ => throw new ArgumentOutOfRangeException(nameof(key))
                }
            },
            DraftValidatorOwnedInvalidVariant.ProposedVersionMissing => CreateSchema() with
            {
                Operation = key == EvidenceVectorKey.Create ? DescriptorDraftOperation.Create : DescriptorDraftOperation.Update
            },
            _ => CreateValidatorOwnedInvalid(variant)
        };

    public ValueTask ResetAsync() => ValueTask.CompletedTask;

    private static List<DescriptorPayloadObservationLeaf> Header(Draft draft)
        => new()
        {
            new("TenantId", ObservationValueKind.Text, draft.TenantId, null, null, null),
            new("DraftId", ObservationValueKind.Text, draft.DraftId, null, null, null),
            new("DescriptorKind", ObservationValueKind.EnumUnderlyingValue, null, (int)draft.DescriptorKind, null, null),
            new("CreatedAt.UtcTicks", ObservationValueKind.Ticks, null, draft.CreatedAt.UtcTicks, null, null)
        };

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

    private static Draft CreateSchema()
        => PostgreSqlControlPlaneReferenceDataJsonCodecTests.CreateDraft(DescriptorKind.Schema);

    private static string? InvalidText(EvidenceVectorKey key)
        => key switch
        {
            EvidenceVectorKey.Null => null,
            EvidenceVectorKey.Empty => string.Empty,
            EvidenceVectorKey.Whitespace => "   ",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
        };
}
