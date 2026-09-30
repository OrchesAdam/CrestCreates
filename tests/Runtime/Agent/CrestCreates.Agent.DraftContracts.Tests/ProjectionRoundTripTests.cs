using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.DraftContracts.Tests;

/// <summary>
/// Round-trip tests: domain → DTO → domain preserves all editable fields
/// for each of the 6 descriptor kinds.
/// </summary>
public class ProjectionRoundTripTests
{
    // ═══════════════════════════════════════════════════════════
    // Capability
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Capability_RoundTrip_Preserves_All_EditableFields()
    {
        var original = new CapabilityDescriptor
        {
            Name = "MyCapability",
            State = DescriptorState.Active,
            Version = 3,
            CapabilityKind = CapabilityKind.Command,
            RiskLevel = CapabilityRiskLevel.High,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("input-schema-1", 1),
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("output-schema-1", 2),
            Consumes = new[] { new EventRef("event", "evt-a", 5), new EventRef("event", "evt-b") },
            Produces = new[] { new EventRef("event", "evt-x", 3) },
        };

        var payload = new CapabilityDescriptorDraftPayload(original);

        var fromResult = AgentDraftPayloadProjection.FromDomain(payload);
        fromResult.IsSuccess.Should().BeTrue();

        var createResult = AgentDraftPayloadProjection.Create(fromResult.Value!, "capability.roundtrip");
        createResult.IsSuccess.Should().BeTrue();

        var roundTripped = (CapabilityDescriptorDraftPayload)createResult.Value!;
        var rt = roundTripped.Descriptor;

        rt.Id.Should().Be("capability.roundtrip");
        rt.Name.Should().Be("MyCapability");
        rt.State.Should().Be(DescriptorState.Active);
//         rt.ContractHash.Should().Be("cap-ch-abc");
//         rt.DefinitionHash.Should().Be("cap-dh-xyz");
        rt.Version.Should().Be(3);
        rt.CapabilityKind.Should().Be(CapabilityKind.Command);
        rt.RiskLevel.Should().Be(CapabilityRiskLevel.High);

        rt.InputSchema.Should().NotBeNull();
        rt.InputSchema!.Value.Id.Should().Be("input-schema-1");
        rt.InputSchema.Value.Version.Should().Be(1);

        rt.OutputSchema.Should().NotBeNull();
        rt.OutputSchema!.Value.Id.Should().Be("output-schema-1");
        rt.OutputSchema.Value.Version.Should().Be(2);

        rt.Consumes.Should().HaveCount(2);
        rt.Consumes[0].Namespace.Should().Be("event");
        rt.Consumes[0].Id.Should().Be("evt-a");
        rt.Consumes[0].Version.Should().Be(5);
        rt.Consumes[1].Namespace.Should().Be("event");
        rt.Consumes[1].Id.Should().Be("evt-b");
        rt.Consumes[1].Version.Should().BeNull();

        rt.Produces.Should().HaveCount(1);
        rt.Produces[0].Namespace.Should().Be("event");
        rt.Produces[0].Id.Should().Be("evt-x");
        rt.Produces[0].Version.Should().Be(3);

        AssertMergePreservesIdentity(roundTripped);
    }

    // ═══════════════════════════════════════════════════════════
    // Workflow
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Workflow_RoundTrip_Preserves_All_EditableFields()
    {
        var original = new WorkflowDescriptor
        {
            Name = "MyWorkflow",
            State = DescriptorState.Draft,
            Version = 7,
            VariableSchema = new VersionedDescriptorRef<SchemaDescriptor>("var-schema-1", 3),
        };

        var payload = new WorkflowDescriptorDraftPayload(original);

        var fromResult = AgentDraftPayloadProjection.FromDomain(payload);
        fromResult.IsSuccess.Should().BeTrue();

        var createResult = AgentDraftPayloadProjection.Create(fromResult.Value!, "workflow.roundtrip");
        createResult.IsSuccess.Should().BeTrue();

        var roundTripped = (WorkflowDescriptorDraftPayload)createResult.Value!;
        var rt = roundTripped.Descriptor;

        rt.Id.Should().Be("workflow.roundtrip");
        rt.Name.Should().Be("MyWorkflow");
        rt.State.Should().Be(DescriptorState.Draft);
//         rt.ContractHash.Should().Be("wf-ch");
//         rt.DefinitionHash.Should().Be("wf-dh");
        rt.Version.Should().Be(7);

        rt.VariableSchema.Should().NotBeNull();
        rt.VariableSchema!.Value.Id.Should().Be("var-schema-1");
        rt.VariableSchema.Value.Version.Should().Be(3);

        AssertMergePreservesIdentity(roundTripped);
    }

    // ═══════════════════════════════════════════════════════════
    // HumanTask
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void HumanTask_RoundTrip_Preserves_All_EditableFields()
    {
        var original = new HumanTaskDescriptor
        {
            Name = "MyHumanTask",
            State = DescriptorState.Active,
            Version = 5,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("interaction-form", 1),
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("input-schema-ht", 2),
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("output-schema-ht", 3),
            AssigneeStrategy = AssigneeStrategy.RoundRobin,
            Timeout = TimeSpan.FromMinutes(30),
        };

        var payload = new HumanTaskDescriptorDraftPayload(original);

        var fromResult = AgentDraftPayloadProjection.FromDomain(payload);
        fromResult.IsSuccess.Should().BeTrue();

        var createResult = AgentDraftPayloadProjection.Create(fromResult.Value!, "human-task.roundtrip");
        createResult.IsSuccess.Should().BeTrue();

        var roundTripped = (HumanTaskDescriptorDraftPayload)createResult.Value!;
        var rt = roundTripped.Descriptor;

        rt.Id.Should().Be("human-task.roundtrip");
        rt.Name.Should().Be("MyHumanTask");
        rt.State.Should().Be(DescriptorState.Active);
//         rt.ContractHash.Should().Be("ht-ch");
//         rt.DefinitionHash.Should().Be("ht-dh");
        rt.Version.Should().Be(5);
        rt.AssigneeStrategy.Should().Be(AssigneeStrategy.RoundRobin);
        rt.Timeout.Should().Be(TimeSpan.FromMinutes(30));

        rt.Interaction.Id.Should().Be("interaction-form");
        rt.Interaction.Version.Should().Be(1);

        rt.InputSchema.Should().NotBeNull();
        rt.InputSchema!.Value.Id.Should().Be("input-schema-ht");
        rt.InputSchema.Value.Version.Should().Be(2);

        rt.OutputSchema.Should().NotBeNull();
        rt.OutputSchema!.Value.Id.Should().Be("output-schema-ht");
        rt.OutputSchema.Value.Version.Should().Be(3);

        AssertMergePreservesIdentity(roundTripped);
    }

    // ═══════════════════════════════════════════════════════════
    // Form
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Form_RoundTrip_Preserves_All_EditableFields()
    {
        var original = new FormDescriptor
        {
            Name = "MyForm",
            State = DescriptorState.Active,
            Version = 2,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("form-schema-1", 1),
        };

        var payload = new FormDescriptorDraftPayload(original);

        var fromResult = AgentDraftPayloadProjection.FromDomain(payload);
        fromResult.IsSuccess.Should().BeTrue();

        var createResult = AgentDraftPayloadProjection.Create(fromResult.Value!, "form.roundtrip");
        createResult.IsSuccess.Should().BeTrue();

        var roundTripped = (FormDescriptorDraftPayload)createResult.Value!;
        var rt = roundTripped.Descriptor;

        rt.Id.Should().Be("form.roundtrip");
        rt.Name.Should().Be("MyForm");
        rt.State.Should().Be(DescriptorState.Active);
//         rt.ContractHash.Should().Be("form-ch");
//         rt.DefinitionHash.Should().Be("form-dh");
        rt.Version.Should().Be(2);

        rt.Schema.Id.Should().Be("form-schema-1");
        rt.Schema.Version.Should().Be(1);

        AssertMergePreservesIdentity(roundTripped);
    }

    // ═══════════════════════════════════════════════════════════
    // Event
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Event_RoundTrip_Preserves_All_EditableFields()
    {
        var original = new EventDescriptor
        {
            Name = "MyEvent",
            State = DescriptorState.Deprecated,
            Version = 4,
            Category = EventCategory.Domain,
            Semantic = EventSemantic.StateTransition,
            Importance = EventImportance.Business,
            ChangeKind = SchemaChangeKind.Breaking,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("payload-schema-1", 1),
        };

        var payload = new EventDescriptorDraftPayload(original);

        var fromResult = AgentDraftPayloadProjection.FromDomain(payload);
        fromResult.IsSuccess.Should().BeTrue();

        var createResult = AgentDraftPayloadProjection.Create(fromResult.Value!, "event.roundtrip");
        createResult.IsSuccess.Should().BeTrue();

        var roundTripped = (EventDescriptorDraftPayload)createResult.Value!;
        var rt = roundTripped.Descriptor;

        rt.Id.Should().Be("event.roundtrip");
        rt.Name.Should().Be("MyEvent");
        rt.State.Should().Be(DescriptorState.Deprecated);
//         rt.ContractHash.Should().Be("ev-ch");
//         rt.DefinitionHash.Should().Be("ev-dh");
        rt.Version.Should().Be(4);
        rt.Category.Should().Be(EventCategory.Domain);
        rt.Semantic.Should().Be(EventSemantic.StateTransition);
        rt.Importance.Should().Be(EventImportance.Business);
        rt.ChangeKind.Should().Be(SchemaChangeKind.Breaking);
        rt.PayloadSchema.Id.Should().Be("payload-schema-1");
        rt.PayloadSchema.Version.Should().Be(1);

        AssertMergePreservesIdentity(roundTripped);
    }

    // ═══════════════════════════════════════════════════════════
    // Schema
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Schema_RoundTrip_Preserves_All_EditableFields()
    {
        var original = new SchemaDescriptor
        {
            Name = "MySchema",
            State = DescriptorState.Active,
            Version = 6,
            ChangeKind = SchemaChangeKind.Additive,
        };

        var payload = new SchemaDescriptorDraftPayload(original);

        var fromResult = AgentDraftPayloadProjection.FromDomain(payload);
        fromResult.IsSuccess.Should().BeTrue();

        var createResult = AgentDraftPayloadProjection.Create(fromResult.Value!, "schema.roundtrip");
        createResult.IsSuccess.Should().BeTrue();

        var roundTripped = (SchemaDescriptorDraftPayload)createResult.Value!;
        var rt = roundTripped.Descriptor;

        rt.Id.Should().Be("schema.roundtrip");
        rt.Name.Should().Be("MySchema");
        rt.State.Should().Be(DescriptorState.Active);
//         rt.ContractHash.Should().Be("sc-ch");
//         rt.DefinitionHash.Should().Be("sc-dh");
        rt.Version.Should().Be(6);
        rt.ChangeKind.Should().Be(SchemaChangeKind.Additive);

        AssertMergePreservesIdentity(roundTripped);
    }

    private static void AssertMergePreservesIdentity(DescriptorDraftPayload existing)
    {
        var dto = AgentDraftPayloadProjection.FromDomain(existing);
        dto.IsSuccess.Should().BeTrue();

        var patch = dto.Value!.Discriminator switch
        {
            DescriptorKind.Capability => new AgentDraftPayloadPatchDto
            {
                Discriminator = DescriptorKind.Capability,
                Capability = new AgentCapabilityDraftPayloadPatchDto
                {
                    Payload = dto.Value.Capability! with { Name = dto.Value.Capability.Name + "-merged" },
                    ChangedFields = AgentCapabilityDraftChangedField.Name
                }
            },
            DescriptorKind.Workflow => new AgentDraftPayloadPatchDto
            {
                Discriminator = DescriptorKind.Workflow,
                Workflow = new AgentWorkflowDraftPayloadPatchDto
                {
                    Payload = dto.Value.Workflow! with { Name = dto.Value.Workflow.Name + "-merged" },
                    ChangedFields = AgentWorkflowDraftChangedField.Name
                }
            },
            DescriptorKind.HumanTask => new AgentDraftPayloadPatchDto
            {
                Discriminator = DescriptorKind.HumanTask,
                HumanTask = new AgentHumanTaskDraftPayloadPatchDto
                {
                    Payload = dto.Value.HumanTask! with { Name = dto.Value.HumanTask.Name + "-merged" },
                    ChangedFields = AgentHumanTaskDraftChangedField.Name
                }
            },
            DescriptorKind.Form => new AgentDraftPayloadPatchDto
            {
                Discriminator = DescriptorKind.Form,
                Form = new AgentFormDraftPayloadPatchDto
                {
                    Payload = dto.Value.Form! with { Name = dto.Value.Form.Name + "-merged" },
                    ChangedFields = AgentFormDraftChangedField.Name
                }
            },
            DescriptorKind.Event => new AgentDraftPayloadPatchDto
            {
                Discriminator = DescriptorKind.Event,
                Event = new AgentEventDraftPayloadPatchDto
                {
                    Payload = dto.Value.Event! with { Name = dto.Value.Event.Name + "-merged" },
                    ChangedFields = AgentEventDraftChangedField.Name
                }
            },
            DescriptorKind.Schema => new AgentDraftPayloadPatchDto
            {
                Discriminator = DescriptorKind.Schema,
                Schema = new AgentSchemaDraftPayloadPatchDto
                {
                    Payload = dto.Value.Schema! with { Name = dto.Value.Schema.Name + "-merged" },
                    ChangedFields = AgentSchemaDraftChangedField.Name
                }
            },
            _ => throw new InvalidOperationException($"Unsupported descriptor kind: {dto.Value.Discriminator}")
        };

        var result = AgentDraftPayloadProjection.Merge(patch, existing);
        result.IsSuccess.Should().BeTrue();
        result.Value!.GetDescriptor().Id.Should().Be(existing.GetDescriptor().Id);
        result.Value.GetDescriptor().Name.Should().Be(existing.GetDescriptor().Name + "-merged");
    }
}
