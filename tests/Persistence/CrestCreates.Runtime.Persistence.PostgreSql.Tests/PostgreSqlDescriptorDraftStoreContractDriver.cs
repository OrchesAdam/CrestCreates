using System.Collections.Immutable;
using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Runtime.Persistence.PostgreSql;
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
        => new(variant, new[]
        {
            new DescriptorPayloadObservationLeaf("TenantId", ObservationValueKind.Text, draft.TenantId, null, null, null),
            new DescriptorPayloadObservationLeaf("DraftId", ObservationValueKind.Text, draft.DraftId, null, null, null),
            new DescriptorPayloadObservationLeaf("DescriptorKind", ObservationValueKind.EnumUnderlyingValue, null, (int)draft.DescriptorKind, null, null),
            new DescriptorPayloadObservationLeaf("CreatedAt.UtcTicks", ObservationValueKind.Ticks, null, draft.CreatedAt.UtcTicks, null, null)
        }.ToImmutableArray());

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
