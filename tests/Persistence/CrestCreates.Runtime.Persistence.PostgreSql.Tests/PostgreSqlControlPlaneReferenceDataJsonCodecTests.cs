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
using FluentAssertions;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

public sealed class PostgreSqlControlPlaneReferenceDataJsonCodecTests
{
    [Theory]
    [InlineData(DescriptorKind.Schema)]
    [InlineData(DescriptorKind.Form)]
    [InlineData(DescriptorKind.Capability)]
    [InlineData(DescriptorKind.HumanTask)]
    [InlineData(DescriptorKind.Event)]
    public void Draft_codec_round_trips_each_concrete_payload(DescriptorKind kind)
    {
        var draft = CreateDraft(kind);

        var restored = PostgreSqlControlPlaneReferenceDataJsonCodec.Deserialize(
            PostgreSqlControlPlaneReferenceDataJsonCodec.Serialize(draft));

        restored.Should().BeEquivalentTo(draft);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Draft_codec_round_trips_each_workflow_target(int targetTypeValue)
    {
        var targetType = (PostgreSqlWorkflowTargetType)targetTypeValue;
        var draft = CreateWorkflowDraft(targetType);

        var restored = PostgreSqlControlPlaneReferenceDataJsonCodec.Deserialize(
            PostgreSqlControlPlaneReferenceDataJsonCodec.Serialize(draft));

        restored.Should().BeEquivalentTo(draft);
    }

    [Fact]
    public void Draft_codec_rejects_missing_payload_arm_with_typed_exception()
    {
        var action = () => PostgreSqlControlPlaneReferenceDataJsonCodec.Deserialize("""
            {
              "contractVersion": 1,
              "tenantId": "tenant-1",
              "draftId": "draft-1",
              "payloadType": 1,
              "schema": null
            }
            """);

        action.Should().Throw<RuntimePersistenceContractException>()
            .Which.Code.Should().Be(RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Fact]
    public void Draft_codec_preserves_validator_owned_null_identifiers()
    {
        var draft = CreateDraft(DescriptorKind.Schema) with
        {
            DescriptorId = null!,
            AuthorId = null!
        };

        var restored = PostgreSqlControlPlaneReferenceDataJsonCodec.Deserialize(
            PostgreSqlControlPlaneReferenceDataJsonCodec.Serialize(draft));

        restored.DescriptorId.Should().BeNull();
        restored.AuthorId.Should().BeNull();
    }

    [Fact]
    public void Organization_roots_use_generated_type_info()
    {
        var value = new OrganizationUnit
        {
            Id = "unit-1",
            TenantId = "tenant-1",
            Name = "Unit",
            Code = "U-1",
            ParentId = "parent-1",
            SortOrder = 2,
            IsActive = false,
            CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2))
        };

        var json = JsonSerializer.Serialize(
            value,
            PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.OrganizationUnit);
        var restored = JsonSerializer.Deserialize(
            json,
            PostgreSqlControlPlaneReferenceDataJsonSerializerContext.Default.OrganizationUnit);

        restored.Should().BeEquivalentTo(value);
    }

    internal static Draft CreateDraft(DescriptorKind kind)
    {
        DescriptorDraftPayload payload = kind switch
        {
            DescriptorKind.Schema => new SchemaDescriptorDraftPayload(new SchemaDescriptor
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
            }),
            DescriptorKind.Form => new FormDescriptorDraftPayload(new FormDescriptor
            {
                Id = "form-1",
                Name = "Form",
                Schema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "schema-1", Version = 1 },
                Fields = new[]
                {
                    new FormFieldDescriptor
                    {
                        SchemaFieldName = "Address",
                        Label = "Address",
                        Metadata = new Dictionary<string, string> { ["display"] = "compact" }
                    }
                }
            }),
            DescriptorKind.Capability => new CapabilityDescriptorDraftPayload(new CapabilityDescriptor
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
            }),
            DescriptorKind.HumanTask => new HumanTaskDescriptorDraftPayload(new HumanTaskDescriptor
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
            }),
            DescriptorKind.Event => new EventDescriptorDraftPayload(new EventDescriptor
            {
                Id = "event-1",
                Name = "Event",
                PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("payload", 1),
                Category = EventCategory.Domain,
                Semantic = EventSemantic.Fact,
                Importance = EventImportance.Business,
                ChangeKind = SchemaChangeKind.Breaking
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        return new Draft
        {
            TenantId = "tenant-1",
            DraftId = $"draft-{kind}",
            DescriptorKind = kind,
            DescriptorId = $"{kind}-descriptor",
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
            Payload = payload
        };
    }

    internal static Draft CreateWorkflowDraft(PostgreSqlWorkflowTargetType targetType)
    {
        InteractionTarget target = targetType switch
        {
            PostgreSqlWorkflowTargetType.Capability => new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "capability-1", Version = 1 }
            },
            PostgreSqlWorkflowTargetType.HumanTask => new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor> { Id = "task-1", Version = 1 }
            },
            PostgreSqlWorkflowTargetType.SubWorkflow => new SubWorkflowTarget
            {
                SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor> { Id = "workflow-child", Version = 1 }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(targetType), targetType, null)
        };

        return new Draft
        {
            TenantId = "tenant-1",
            DraftId = $"draft-workflow-{targetType}",
            DescriptorKind = DescriptorKind.Workflow,
            DescriptorId = "workflow-descriptor",
            Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Human,
            AuthorId = "author-1",
            CreatedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(5)),
            Payload = new WorkflowDescriptorDraftPayload(new WorkflowDescriptor
            {
                Id = "workflow-1",
                Name = "Workflow",
                VariableSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "variables", Version = 1 },
                Steps = new[]
                {
                    new WorkflowStep
                    {
                        Id = "step-1",
                        Name = "Step",
                        Target = target,
                        Condition = "ready",
                        Transitions = new[] { "step-2" },
                        InputMapping = "input",
                        OutputMapping = "output",
                        OnError = StepErrorBehavior.Skip
                    }
                },
                DefaultVariableScope = WorkflowVariableScope.SubWorkflow
            })
        };
    }
}
