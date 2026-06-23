using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Validation tests for the Merge projection method (P0-4 and P0-5),
/// exercising the generated projection directly from the ControlPlane test context.
/// </summary>
public class AgentDraftPayloadPatchValidationTests
{
    // ═══════════════════════════════════════════════════════════
    //  P0-4: Unknown ChangedFields bits
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Merge_UnknownChangedFieldBits_Returns_Error()
    {
        var existingDto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadDto
            {
                Name = "Test",
                State = DescriptorState.Active,
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Low,
            },
        };
        var createResult = AgentDraftPayloadProjection.Create(existingDto);
        createResult.IsSuccess.Should().BeTrue();
        var existingPayload = createResult.Value!;

        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = new AgentCapabilityDraftPayloadDto
                {
                    Name = "Updated",
                    State = DescriptorState.Active,
                    CapabilityKind = CapabilityKind.Command,
                    RiskLevel = CapabilityRiskLevel.Low,
                },
                ChangedFields = (AgentCapabilityDraftChangedField)2048,
            },
        };

        var mergeResult = AgentDraftPayloadProjection.Merge(patch, existingPayload);
        mergeResult.IsSuccess.Should().BeFalse();
        mergeResult.Errors.Should().Contain(e => e.Code == AgentDraftContractErrorCodes.UnknownChangedField);
    }

    [Fact]
    public void Merge_NullForNonNullableField_Returns_Error()
    {
        // Arrange: Create existing payload
        var existingDto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadDto
            {
                Name = "OriginalName",
                State = DescriptorState.Active,
                Version = 5,
                CapabilityKind = CapabilityKind.Query,
                RiskLevel = CapabilityRiskLevel.Medium,
            },
        };
        var createResult = AgentDraftPayloadProjection.Create(existingDto);
        createResult.IsSuccess.Should().BeTrue();
        var existingPayload = createResult.Value!;

        // Act: Patch Name with null — Name is non-nullable string domain
        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = new AgentCapabilityDraftPayloadDto
                {
                    Name = null!,
                    State = DescriptorState.Active,
                    CapabilityKind = CapabilityKind.Command,
                    RiskLevel = CapabilityRiskLevel.Low,
                },
                ChangedFields = AgentCapabilityDraftChangedField.Name,
            },
        };

        var mergeResult = AgentDraftPayloadProjection.Merge(patch, existingPayload);
        mergeResult.IsSuccess.Should().BeFalse();
        mergeResult.Errors.Should().Contain(e => e.Code == AgentDraftContractErrorCodes.NonNullableFieldNull);
    }

    [Fact]
    public void Merge_NullForNullableField_ClearsToNull()
    {
        // Arrange: Create existing payload with InputSchema set
        var existingDto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadDto
            {
                Name = "Test",
                State = DescriptorState.Active,
                CapabilityKind = CapabilityKind.Query,
                RiskLevel = CapabilityRiskLevel.Low,
                InputSchema = new DescriptorRef("schema", "test-schema", 1),
            },
        };
        var createResult = AgentDraftPayloadProjection.Create(existingDto);
        createResult.IsSuccess.Should().BeTrue();
        var existingPayload = createResult.Value!;

        // Act: Patch InputSchema with null — nullable domain ref, should clear to null
        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = new AgentCapabilityDraftPayloadDto
                {
                    Name = "Test",
                    State = DescriptorState.Active,
                    CapabilityKind = CapabilityKind.Query,
                    RiskLevel = CapabilityRiskLevel.Low,
                    InputSchema = null,
                },
                ChangedFields = AgentCapabilityDraftChangedField.InputSchema,
            },
        };

        var mergeResult = AgentDraftPayloadProjection.Merge(patch, existingPayload);
        mergeResult.IsSuccess.Should().BeTrue();

        // Assert: Domain InputSchema cleared to null
        var fromDomainResult = AgentDraftPayloadProjection.FromDomain(mergeResult.Value!);
        fromDomainResult.IsSuccess.Should().BeTrue();
        var result = fromDomainResult.Value!;
        result.Capability!.InputSchema.Should().BeNull();
    }

    [Fact]
    public void Merge_Event_NullForNonNullableRef_Returns_Error()
    {
        // Arrange: Create existing Event payload with PayloadSchema set
        var existingDto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Event,
            Event = new AgentEventDraftPayloadDto
            {
                Name = "TestEvent",
                State = DescriptorState.Active,
                Category = EventCategory.Domain,
                Semantic = EventSemantic.Fact,
                Importance = EventImportance.Critical,
                ChangeKind = SchemaChangeKind.Additive,
                PayloadSchema = new DescriptorRef("schema", "event-ps", 1),
            },
        };
        var createResult = AgentDraftPayloadProjection.Create(existingDto);
        createResult.IsSuccess.Should().BeTrue();
        var existingPayload = createResult.Value!;

        // Act: Patch PayloadSchema with null — non-nullable VersionedDescriptorRef
        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Event,
            Event = new AgentEventDraftPayloadPatchDto
            {
                Payload = new AgentEventDraftPayloadDto
                {
                    Name = "TestEvent",
                    State = DescriptorState.Active,
                    Category = EventCategory.Domain,
                    Semantic = EventSemantic.Fact,
                    Importance = EventImportance.Critical,
                    ChangeKind = SchemaChangeKind.Additive,
                    PayloadSchema = null,
                },
                ChangedFields = AgentEventDraftChangedField.PayloadSchema,
            },
        };

        var mergeResult = AgentDraftPayloadProjection.Merge(patch, existingPayload);
        mergeResult.IsSuccess.Should().BeFalse();
        mergeResult.Errors.Should().Contain(e => e.Code == AgentDraftContractErrorCodes.NonNullableFieldNull);
        mergeResult.Errors.Should().Contain(e => e.Message!.Contains("PayloadSchema"));
    }

    [Fact]
    public void Merge_Integration_ValidNonEmptyChangedFields_Succeeds()
    {
        // Verify that when all ChangedFields bits are valid, merge proceeds normally
        var existingDto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadDto
            {
                Name = "OriginalName",
                State = DescriptorState.Active,
                Version = 5,
                CapabilityKind = CapabilityKind.Query,
                RiskLevel = CapabilityRiskLevel.Medium,
            },
        };
        var createResult = AgentDraftPayloadProjection.Create(existingDto);
        createResult.IsSuccess.Should().BeTrue();
        var existingPayload = createResult.Value!;

        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = new AgentCapabilityDraftPayloadDto
                {
                    Name = "UpdatedName",
                    State = DescriptorState.Active,
                    CapabilityKind = CapabilityKind.Command,
                    RiskLevel = CapabilityRiskLevel.Low,
                },
                ChangedFields = AgentCapabilityDraftChangedField.Name,
            },
        };

        var mergeResult = AgentDraftPayloadProjection.Merge(patch, existingPayload);
        mergeResult.IsSuccess.Should().BeTrue();

        var fromDomainResult = AgentDraftPayloadProjection.FromDomain(mergeResult.Value!);
        fromDomainResult.IsSuccess.Should().BeTrue();
        var result = fromDomainResult.Value!;
        result.Capability!.Name.Should().Be("UpdatedName");
    }
}
