namespace CrestCreates.Agent.DraftContracts.Tests;

using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.Event.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Round-trip fidelity tests: DTO → domain → DTO preserves all editable fields
/// for each of the 6 descriptor kinds.
/// </summary>
public class AgentDraftPayloadRoundTripTests
{
    /// <summary>
    /// Creates a DescriptorRef with known values.
    /// Version should be set explicitly to avoid the null→1 fallback
    /// during Create, which would make the round-trip appear to mutate the value.
    /// </summary>
    private static DescriptorRef MakeRef(string ns, string id, int? version = 1)
        => new(ns, id, version);

    // ═══════════════════════════════════════════════════════════
    // Capability
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Capability_RoundTrip_Preserves_All_Editable_Fields()
    {
        var dto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadDto
            {
                Name = "TestCapability",
                State = DescriptorState.Active,
                Version = 3,
                ContractHash = "hash-cap-123",
                DefinitionHash = "defhash-cap-456",
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.High,
                InputSchema = MakeRef("schema", "input-schema", 2),
                OutputSchema = MakeRef("schema", "output-schema", 1),
                Produces = new[] { MakeRef("event", "produced-event", 1) },
                Consumes = new[] { MakeRef("event", "consumed-event", 1) },
            }
        };

        // Act: DTO → domain → DTO
        var createResult = AgentDraftPayloadProjection.Create(dto);
        createResult.IsSuccess.Should().BeTrue();

        var fromResult = AgentDraftPayloadProjection.FromDomain(createResult.Value!);
        fromResult.IsSuccess.Should().BeTrue();

        var result = fromResult.Value!;
        result.Discriminator.Should().Be(DescriptorKind.Capability);
        result.Capability.Should().NotBeNull();

        var c = result.Capability!;
        c.Name.Should().Be("TestCapability");
        c.State.Should().Be(DescriptorState.Active);
        c.Version.Should().Be(3);
        c.ContractHash.Should().Be("hash-cap-123");
        c.DefinitionHash.Should().Be("defhash-cap-456");
        c.CapabilityKind.Should().Be(CapabilityKind.Command);
        c.RiskLevel.Should().Be(CapabilityRiskLevel.High);

        c.InputSchema.Should().NotBeNull();
        c.InputSchema!.Value.Namespace.Should().Be("schema");
        c.InputSchema!.Value.Id.Should().Be("input-schema");
        c.InputSchema!.Value.Version.Should().Be(2);

        c.OutputSchema.Should().NotBeNull();
        c.OutputSchema!.Value.Namespace.Should().Be("schema");
        c.OutputSchema!.Value.Id.Should().Be("output-schema");
        c.OutputSchema!.Value.Version.Should().Be(1);

        c.Produces.Should().NotBeNull();
        c.Produces![0].Namespace.Should().Be("event");
        c.Produces![0].Id.Should().Be("produced-event");
        c.Produces![0].Version.Should().Be(1);

        c.Consumes.Should().NotBeNull();
        c.Consumes![0].Namespace.Should().Be("event");
        c.Consumes![0].Id.Should().Be("consumed-event");
        c.Consumes![0].Version.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════
    // Event
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Event_RoundTrip_Preserves_All_Editable_Fields()
    {
        var dto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Event,
            Event = new AgentEventDraftPayloadDto
            {
                Name = "TestEvent",
                State = DescriptorState.Deprecated,
                Version = 4,
                ContractHash = "hash-ev-001",
                DefinitionHash = "defhash-ev-002",
                Category = EventCategory.Domain,
                Semantic = EventSemantic.StateTransition,
                Importance = EventImportance.Business,
                ChangeKind = SchemaChangeKind.Breaking,
                PayloadSchema = MakeRef("schema", "payload-schema", 1),
            }
        };

        // Act: DTO → domain → DTO
        var createResult = AgentDraftPayloadProjection.Create(dto);
        createResult.IsSuccess.Should().BeTrue();

        var fromResult = AgentDraftPayloadProjection.FromDomain(createResult.Value!);
        fromResult.IsSuccess.Should().BeTrue();

        var result = fromResult.Value!;
        result.Discriminator.Should().Be(DescriptorKind.Event);
        result.Event.Should().NotBeNull();

        var e = result.Event!;
        e.Name.Should().Be("TestEvent");
        e.State.Should().Be(DescriptorState.Deprecated);
        e.Version.Should().Be(4);
        e.ContractHash.Should().Be("hash-ev-001");
        e.DefinitionHash.Should().Be("defhash-ev-002");
        e.Category.Should().Be(EventCategory.Domain);
        e.Semantic.Should().Be(EventSemantic.StateTransition);
        e.Importance.Should().Be(EventImportance.Business);
        e.ChangeKind.Should().Be(SchemaChangeKind.Breaking);

        e.PayloadSchema.Should().NotBeNull();
        e.PayloadSchema!.Value.Namespace.Should().Be("schema");
        e.PayloadSchema!.Value.Id.Should().Be("payload-schema");
        e.PayloadSchema!.Value.Version.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════
    // Form
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Form_RoundTrip_Preserves_All_Editable_Fields()
    {
        var dto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Form,
            Form = new AgentFormDraftPayloadDto
            {
                Name = "TestForm",
                State = DescriptorState.Active,
                Version = 2,
                ContractHash = "hash-form-1",
                DefinitionHash = "defhash-form-2",
                FormSchema = MakeRef("schema", "form-schema-1", 1),
            }
        };

        // Act: DTO → domain → DTO
        var createResult = AgentDraftPayloadProjection.Create(dto);
        createResult.IsSuccess.Should().BeTrue();

        var fromResult = AgentDraftPayloadProjection.FromDomain(createResult.Value!);
        fromResult.IsSuccess.Should().BeTrue();

        var result = fromResult.Value!;
        result.Discriminator.Should().Be(DescriptorKind.Form);
        result.Form.Should().NotBeNull();

        var f = result.Form!;
        f.Name.Should().Be("TestForm");
        f.State.Should().Be(DescriptorState.Active);
        f.Version.Should().Be(2);
        f.ContractHash.Should().Be("hash-form-1");
        f.DefinitionHash.Should().Be("defhash-form-2");

        f.FormSchema.Should().NotBeNull();
        f.FormSchema!.Value.Namespace.Should().Be("schema");
        f.FormSchema!.Value.Id.Should().Be("form-schema-1");
        f.FormSchema!.Value.Version.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════
    // HumanTask
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void HumanTask_RoundTrip_Preserves_All_Editable_Fields()
    {
        var dto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.HumanTask,
            HumanTask = new AgentHumanTaskDraftPayloadDto
            {
                Name = "TestHumanTask",
                State = DescriptorState.Active,
                Version = 5,
                ContractHash = "hash-ht-01",
                DefinitionHash = "defhash-ht-02",
                AssigneeStrategy = AssigneeStrategy.RoundRobin,
                Timeout = "00:30:00",
                Interaction = MakeRef("form", "interaction-form", 1),
                InputSchema = MakeRef("schema", "input-schema-ht", 2),
                OutputSchema = MakeRef("schema", "output-schema-ht", 3),
            }
        };

        // Act: DTO → domain → DTO
        var createResult = AgentDraftPayloadProjection.Create(dto);
        createResult.IsSuccess.Should().BeTrue();

        var fromResult = AgentDraftPayloadProjection.FromDomain(createResult.Value!);
        fromResult.IsSuccess.Should().BeTrue();

        var result = fromResult.Value!;
        result.Discriminator.Should().Be(DescriptorKind.HumanTask);
        result.HumanTask.Should().NotBeNull();

        var ht = result.HumanTask!;
        ht.Name.Should().Be("TestHumanTask");
        ht.State.Should().Be(DescriptorState.Active);
        ht.Version.Should().Be(5);
        ht.ContractHash.Should().Be("hash-ht-01");
        ht.DefinitionHash.Should().Be("defhash-ht-02");
        ht.AssigneeStrategy.Should().Be(AssigneeStrategy.RoundRobin);
        ht.Timeout.Should().Be("00:30:00");

        ht.Interaction.Should().NotBeNull();
        ht.Interaction!.Value.Namespace.Should().Be("form");
        ht.Interaction!.Value.Id.Should().Be("interaction-form");
        ht.Interaction!.Value.Version.Should().Be(1);

        ht.InputSchema.Should().NotBeNull();
        ht.InputSchema!.Value.Namespace.Should().Be("schema");
        ht.InputSchema!.Value.Id.Should().Be("input-schema-ht");
        ht.InputSchema!.Value.Version.Should().Be(2);

        ht.OutputSchema.Should().NotBeNull();
        ht.OutputSchema!.Value.Namespace.Should().Be("schema");
        ht.OutputSchema!.Value.Id.Should().Be("output-schema-ht");
        ht.OutputSchema!.Value.Version.Should().Be(3);
    }

    // ═══════════════════════════════════════════════════════════
    // Schema
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Schema_RoundTrip_Preserves_All_Editable_Fields()
    {
        var dto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Schema,
            Schema = new AgentSchemaDraftPayloadDto
            {
                Name = "TestSchema",
                State = DescriptorState.Active,
                Version = 6,
                ContractHash = "hash-sc-1",
                DefinitionHash = "defhash-sc-2",
                ChangeKind = SchemaChangeKind.Additive,
            }
        };

        // Act: DTO → domain → DTO
        var createResult = AgentDraftPayloadProjection.Create(dto);
        createResult.IsSuccess.Should().BeTrue();

        var fromResult = AgentDraftPayloadProjection.FromDomain(createResult.Value!);
        fromResult.IsSuccess.Should().BeTrue();

        var result = fromResult.Value!;
        result.Discriminator.Should().Be(DescriptorKind.Schema);
        result.Schema.Should().NotBeNull();

        var s = result.Schema!;
        s.Name.Should().Be("TestSchema");
        s.State.Should().Be(DescriptorState.Active);
        s.Version.Should().Be(6);
        s.ContractHash.Should().Be("hash-sc-1");
        s.DefinitionHash.Should().Be("defhash-sc-2");
        s.ChangeKind.Should().Be(SchemaChangeKind.Additive);
    }

    // ═══════════════════════════════════════════════════════════
    // Workflow
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Workflow_RoundTrip_Preserves_All_Editable_Fields()
    {
        var dto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Workflow,
            Workflow = new AgentWorkflowDraftPayloadDto
            {
                Name = "TestWorkflow",
                State = DescriptorState.Draft,
                Version = 7,
                ContractHash = "hash-wf-1",
                DefinitionHash = "defhash-wf-2",
                VariableSchema = MakeRef("schema", "var-schema-1", 3),
            }
        };

        // Act: DTO → domain → DTO
        var createResult = AgentDraftPayloadProjection.Create(dto);
        createResult.IsSuccess.Should().BeTrue();

        var fromResult = AgentDraftPayloadProjection.FromDomain(createResult.Value!);
        fromResult.IsSuccess.Should().BeTrue();

        var result = fromResult.Value!;
        result.Discriminator.Should().Be(DescriptorKind.Workflow);
        result.Workflow.Should().NotBeNull();

        var w = result.Workflow!;
        w.Name.Should().Be("TestWorkflow");
        w.State.Should().Be(DescriptorState.Draft);
        w.Version.Should().Be(7);
        w.ContractHash.Should().Be("hash-wf-1");
        w.DefinitionHash.Should().Be("defhash-wf-2");

        w.VariableSchema.Should().NotBeNull();
        w.VariableSchema!.Value.Namespace.Should().Be("schema");
        w.VariableSchema!.Value.Id.Should().Be("var-schema-1");
        w.VariableSchema!.Value.Version.Should().Be(3);
    }
}
