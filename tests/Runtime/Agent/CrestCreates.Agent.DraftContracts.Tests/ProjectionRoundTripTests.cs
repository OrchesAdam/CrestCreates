using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
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
            ContractHash = "cap-ch-abc",
            DefinitionHash = "cap-dh-xyz",
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

        var createResult = AgentDraftPayloadProjection.Create(fromResult.Value!);
        createResult.IsSuccess.Should().BeTrue();

        var roundTripped = (CapabilityDescriptorDraftPayload)createResult.Value!;
        var rt = roundTripped.Descriptor;

        rt.Name.Should().Be("MyCapability");
        rt.State.Should().Be(DescriptorState.Active);
        rt.ContractHash.Should().Be("cap-ch-abc");
        rt.DefinitionHash.Should().Be("cap-dh-xyz");
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
            ContractHash = "wf-ch",
            DefinitionHash = "wf-dh",
            Version = 7,
            VariableSchema = new VersionedDescriptorRef<SchemaDescriptor>("var-schema-1", 3),
        };

        var payload = new WorkflowDescriptorDraftPayload(original);

        var fromResult = AgentDraftPayloadProjection.FromDomain(payload);
        fromResult.IsSuccess.Should().BeTrue();

        var createResult = AgentDraftPayloadProjection.Create(fromResult.Value!);
        createResult.IsSuccess.Should().BeTrue();

        var roundTripped = (WorkflowDescriptorDraftPayload)createResult.Value!;
        var rt = roundTripped.Descriptor;

        rt.Name.Should().Be("MyWorkflow");
        rt.State.Should().Be(DescriptorState.Draft);
        rt.ContractHash.Should().Be("wf-ch");
        rt.DefinitionHash.Should().Be("wf-dh");
        rt.Version.Should().Be(7);

        rt.VariableSchema.Should().NotBeNull();
        rt.VariableSchema!.Value.Id.Should().Be("var-schema-1");
        rt.VariableSchema.Value.Version.Should().Be(3);
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
            ContractHash = "ht-ch",
            DefinitionHash = "ht-dh",
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

        var createResult = AgentDraftPayloadProjection.Create(fromResult.Value!);
        createResult.IsSuccess.Should().BeTrue();

        var roundTripped = (HumanTaskDescriptorDraftPayload)createResult.Value!;
        var rt = roundTripped.Descriptor;

        rt.Name.Should().Be("MyHumanTask");
        rt.State.Should().Be(DescriptorState.Active);
        rt.ContractHash.Should().Be("ht-ch");
        rt.DefinitionHash.Should().Be("ht-dh");
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
            ContractHash = "form-ch",
            DefinitionHash = "form-dh",
            Version = 2,
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("form-schema-1", 1),
        };

        var payload = new FormDescriptorDraftPayload(original);

        var fromResult = AgentDraftPayloadProjection.FromDomain(payload);
        fromResult.IsSuccess.Should().BeTrue();

        var createResult = AgentDraftPayloadProjection.Create(fromResult.Value!);
        createResult.IsSuccess.Should().BeTrue();

        var roundTripped = (FormDescriptorDraftPayload)createResult.Value!;
        var rt = roundTripped.Descriptor;

        rt.Name.Should().Be("MyForm");
        rt.State.Should().Be(DescriptorState.Active);
        rt.ContractHash.Should().Be("form-ch");
        rt.DefinitionHash.Should().Be("form-dh");
        rt.Version.Should().Be(2);

        rt.Schema.Id.Should().Be("form-schema-1");
        rt.Schema.Version.Should().Be(1);
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
            ContractHash = "ev-ch",
            DefinitionHash = "ev-dh",
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

        var createResult = AgentDraftPayloadProjection.Create(fromResult.Value!);
        createResult.IsSuccess.Should().BeTrue();

        var roundTripped = (EventDescriptorDraftPayload)createResult.Value!;
        var rt = roundTripped.Descriptor;

        rt.Name.Should().Be("MyEvent");
        rt.State.Should().Be(DescriptorState.Deprecated);
        rt.ContractHash.Should().Be("ev-ch");
        rt.DefinitionHash.Should().Be("ev-dh");
        rt.Version.Should().Be(4);
        rt.Category.Should().Be(EventCategory.Domain);
        rt.Semantic.Should().Be(EventSemantic.StateTransition);
        rt.Importance.Should().Be(EventImportance.Business);
        rt.ChangeKind.Should().Be(SchemaChangeKind.Breaking);
        rt.PayloadSchema.Id.Should().Be("payload-schema-1");
        rt.PayloadSchema.Version.Should().Be(1);
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
            ContractHash = "sc-ch",
            DefinitionHash = "sc-dh",
            Version = 6,
            ChangeKind = SchemaChangeKind.Additive,
        };

        var payload = new SchemaDescriptorDraftPayload(original);

        var fromResult = AgentDraftPayloadProjection.FromDomain(payload);
        fromResult.IsSuccess.Should().BeTrue();

        var createResult = AgentDraftPayloadProjection.Create(fromResult.Value!);
        createResult.IsSuccess.Should().BeTrue();

        var roundTripped = (SchemaDescriptorDraftPayload)createResult.Value!;
        var rt = roundTripped.Descriptor;

        rt.Name.Should().Be("MySchema");
        rt.State.Should().Be(DescriptorState.Active);
        rt.ContractHash.Should().Be("sc-ch");
        rt.DefinitionHash.Should().Be("sc-dh");
        rt.Version.Should().Be(6);
        rt.ChangeKind.Should().Be(SchemaChangeKind.Additive);
    }
}
